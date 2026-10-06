using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Log;
using Viv.Nana.Options;

namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// <see cref="IMqttClientService"/> 的实现。单例：一个进程对 broker 一条连接。
    /// 断线重连与待发缓冲交给 MQTTnet 的 ManagedClient，不自己写重连循环。
    /// </summary>
    internal sealed class MqttConnection : IMqttClientService, IAsyncDisposable
    {
        private readonly MqttOptions _options;
        private readonly ILoggerContract _logger;
        private readonly IManagedMqttClient _client;
        private readonly MqttSubscriptions _subscriptions = new();

        private readonly SemaphoreSlim _startLock = new(1, 1);
        private bool _started;
        private bool _disposed;

        public MqttConnection(IOptions<MqttOptions> options, ILoggerContract logger)
        {
            _options = options.Value;
            _logger = logger;
            _client = new MqttFactory().CreateManagedMqttClient();
        }

        public bool IsConnected => _client.IsConnected;

        public int PendingMessages => _client.PendingApplicationMessagesCount;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_started) return;

                _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                _client.ConnectedAsync += _ =>
                {
                    _logger.Info("MQTT 已连接：{0}:{1}", _options.Host, _options.Port);
                    return Task.CompletedTask;
                };
                _client.DisconnectedAsync += _ =>
                {
                    _logger.Warning("MQTT 连接断开：{0}:{1}，{2} 秒后重连", _options.Host, _options.Port, _options.ReconnectDelaySeconds);
                    return Task.CompletedTask;
                };
                _client.ConnectingFailedAsync += _ =>
                {
                    NanaMetrics.RecordMqttError("connect");
                    _logger.Error("MQTT 连接失败：{0}:{1}", _options.Host, _options.Port);
                    return Task.CompletedTask;
                };

                var managedOptions = new ManagedMqttClientOptionsBuilder()
                    .WithClientOptions(BuildClientOptions())
                    .WithAutoReconnectDelay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds))
                    .WithMaxPendingMessages(_options.MaxPendingMessages)
                    .Build();

                await _client.StartAsync(managedOptions).ConfigureAwait(false);
                _started = true;
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_started) return;
            _started = false;

            try
            {
                await _client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning("MQTT 断开时出错：{0}", ex.Message);
            }
        }

        public Task PublishJsonAsync<T>(string topic, T payload,
            MqttQualityOfServiceLevel? qos = null, bool? retain = null,
            CancellationToken cancellationToken = default)
            => PublishAsync(topic, JsonSerializer.SerializeToUtf8Bytes(payload), qos, retain, cancellationToken);

        public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload,
            MqttQualityOfServiceLevel? qos = null, bool? retain = null,
            CancellationToken cancellationToken = default)
        {
            var message = new MqttApplicationMessage
            {
                Topic = topic,
                Payload = payload.ToArray(),
                QualityOfServiceLevel = qos ?? _options.DefaultPublishQos,
                Retain = retain ?? _options.DefaultPublishRetain
            };

            try
            {
                await _client.EnqueueAsync(message).ConfigureAwait(false);
                NanaMetrics.RecordMqttPublish(topic);
            }
            catch (Exception ex)
            {
                NanaMetrics.RecordMqttError("publish");
                throw new VivConnectionException(VivConnType.Mqtt, $"{_options.Host}:{_options.Port}",
                    $"MQTT 发布失败：{topic}", ex);
            }
        }

        public async Task<IDisposable> SubscribeAsync(string topicFilter, string? sharedGroup,
            Func<MqttMessage, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(topicFilter);
            ArgumentNullException.ThrowIfNull(handler);

            // 同一个下发过滤器重复订阅会顶掉原处理器，静默丢处理器比报错难查得多，直接拦下
            if (!_subscriptions.TryAdd(topicFilter, sharedGroup, handler, out var entry))
                throw new InvalidOperationException($"MQTT 过滤器已订阅：{MqttSubscriptions.ToWireFilter(topicFilter, sharedGroup)}");

            try
            {
                await _client.SubscribeAsync([new MqttTopicFilterBuilder().WithTopic(entry.WireFilter).Build()]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _subscriptions.Remove(entry.WireFilter);
                NanaMetrics.RecordMqttError("subscribe");
                throw new VivConnectionException(VivConnType.Mqtt, $"{_options.Host}:{_options.Port}",
                    $"MQTT 订阅失败：{entry.WireFilter}", ex);
            }

            return new Subscription(this, entry.WireFilter);
        }

        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var app = e.ApplicationMessage;

            await DispatchAsync(new MqttMessage
            {
                Topic = app.Topic,
                Payload = app.Payload is null ? [] : app.Payload,
                Qos = app.QualityOfServiceLevel,
                Retain = app.Retain,
                UserProperties = app.UserProperties is null
                    ? new Dictionary<string, string>()
                    : app.UserProperties.ToDictionary(p => p.Name, p => p.Value),
                ReceivedAt = DateTimeOffset.UtcNow
            }).ConfigureAwait(false);
        }

        /// <summary>把一条报文分发给所有匹配的订阅。与 MQTTnet 的事件解耦，便于单测。</summary>
        internal async Task DispatchAsync(MqttMessage message)
        {
            NanaMetrics.RecordMqttReceive(message.Topic);

            foreach (var entry in _subscriptions.Matching(message.Topic))
            {
                try
                {
                    await entry.Handler(message, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 处理器异常不能冒泡回 ManagedClient：那会打断整条连接的回调
                    NanaMetrics.RecordMqttError("handler");
                    _logger.Error("MQTT 报文处理失败：{0}，{1}", message.Topic, ex.Message);
                }
            }
        }

        private MqttClientOptions BuildClientOptions()
        {
            var builder = new MqttClientOptionsBuilder()
                .WithProtocolVersion(_options.ProtocolVersion)
                .WithTcpServer(_options.Host, _options.Port)
                .WithClientId(ResolveClientId())
                .WithCleanSession(_options.CleanSession)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithTimeout(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

            if (!string.IsNullOrEmpty(_options.UserName))
            {
                builder = builder.WithCredentials(_options.UserName, _options.Password);
            }

            if (_options.UseTls)
            {
                builder = builder.WithTls();
            }

            return builder.Build();
        }

        private string ResolveClientId()
        {
            if (!string.IsNullOrWhiteSpace(_options.ClientId)) return _options.ClientId;

            var entry = Assembly.GetEntryAssembly()?.GetName().Name ?? "viv";
            return $"viv-{entry}-{Guid.NewGuid():N}";
        }

        public async ValueTask DisposeAsync()
        {
            // 具体类型与接口两条注册都指向本实例，容器可能把同一个对象登记两次 ——
            // 幂等自己保证，别指望容器只释放一次
            if (_disposed) return;
            _disposed = true;

            await StopAsync().ConfigureAwait(false);
            _client.Dispose();
            _startLock.Dispose();
        }

        /// <summary>退订句柄：释放时摘掉处理器并下发退订</summary>
        private sealed class Subscription : IDisposable
        {
            private readonly MqttConnection _owner;
            private readonly string _wireFilter;

            public Subscription(MqttConnection owner, string wireFilter)
            {
                _owner = owner;
                _wireFilter = wireFilter;
            }

            public void Dispose()
            {
                if (_owner._subscriptions.Remove(_wireFilter))
                {
                    _ = _owner._client.UnsubscribeAsync([_wireFilter]);
                }
            }
        }
    }
}
