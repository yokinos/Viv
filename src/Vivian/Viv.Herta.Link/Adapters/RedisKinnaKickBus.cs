using System.Threading.Channels;
using StackExchange.Redis;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;
using Viv.Kinna.Ports;
using Viv.Log;
using Viv.Redis;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 跨副本踢人的 Redis pub/sub 实现。
    ///
    /// 为什么用 pub/sub 而不是业务事件总线：踢人是控制指令，和业务消息共享一条队列时，
    /// 积压会把「封号立即生效」拖成分钟级。这里也不需要持久化 —— 丢一条的代价是
    /// 该用户多连几十秒，Ping 拿不到续租会自己断。
    ///
    /// 频道按 Hub 分开：一个进程装两个 Hub 时，各自的踢人指令不该互相唤醒。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public sealed class RedisKinnaKickBus : IKinnaKickBus<ChatHub>, IDependency
    {
        private static readonly RedisChannel KickChannel = RedisChannel.Literal("kinna:kick:ChatHub");

        /// <summary>
        /// 进程级实例 Id。不能放实例字段：注册成 Scoped 时每次解析都会变一个，
        /// 发布方写进去的 Origin 和订阅端手里的对不上，本机就会被踢两遍。
        /// </summary>
        private static readonly string ProcessInstanceId =
            $"{Environment.MachineName}-{Guid.NewGuid():N}"[..24];

        private readonly IRedisService _redis;
        private readonly ILoggerContract _logger;

        public RedisKinnaKickBus(IRedisService redis, ILoggerContract logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public string InstanceId => ProcessInstanceId;

        public async Task PublishAsync(KinnaKickCommand command, CancellationToken cancellationToken = default)
        {
            // IRedisService.PublishAsync 没有取消参数，这里只能做到「已经取消就别发」——
            // 停机时没必要再为一个命令等一个 RTT
            cancellationToken.ThrowIfCancellationRequested();

            // Origin 由通道填而不是协调器填：协调器不该知道「我是哪个实例」
            command.Origin ??= InstanceId;

            await _redis.PublishAsync(KickChannel, command).ConfigureAwait(false);
        }

        public IDisposable Subscribe(Func<KinnaKickCommand, Task> handler)
        {
            var subscription = new Subscription(_redis, _logger, handler);

            // 回调跑在 Redis 的接收线程上：既不能阻塞，也不能把异常抛回去——
            // 抛出去没人接，整个订阅会静默失效，之后所有踢人指令都收不到。
            // 所以这里只把指令塞进队列，真正的处理由 Subscription 的单读者串行跑：
            // Redis 会并发回调，直接 `_ = handler(...)` 会让两条指令同时进协调器
            _redis.Subscribe<KinnaKickCommand>(KickChannel, command =>
            {
                if (command == null)
                {
                    return;
                }

                subscription.Post(command);
            });

            return subscription;
        }

        /// <summary>
        /// 一次订阅。指令先入有界队列，再按到达顺序逐条交给 handler。
        ///
        /// 串行不只是为了顺序：两条踢人指令同时跑，同一个用户的连接会被摘两次、
        /// 软踢帧会交错着写进待发队列；突发时任务数也没有上限，一条 Redis 消息一个 Task。
        /// </summary>
        private sealed class Subscription : IDisposable
        {
            /// <summary>
            /// 待处理的指令上限。踢人是低频管理动作（封号、强制下线），堆到几百条
            /// 说明副本处理不过来或者在批量操作，这时丢新的比继续堆内存好
            /// </summary>
            private const int QueueCapacity = 256;

            private static readonly string HubName = typeof(ChatHub).Name;

            private readonly IRedisService _redis;
            private readonly ILoggerContract _logger;
            private readonly Channel<KinnaKickCommand> _queue;
            private readonly Task _consumer;
            private int _disposed;

            internal Subscription(IRedisService redis, ILoggerContract logger, Func<KinnaKickCommand, Task> handler)
            {
                _redis = redis;
                _logger = logger;

                _queue = Channel.CreateBounded<KinnaKickCommand>(new BoundedChannelOptions(QueueCapacity)
                {
                    // 这里的组合是刻意的：FullMode 用 Wait，写入一律走 TryWrite。
                    // Wait 模式下队列满时 TryWrite 确定返回 false，调用方据此记「丢弃」；
                    // 换成 DropWrite 的话 TryWrite 永远为 true，判不出到底丢没丢。
                    // 绝不 await WriteAsync —— 等就是堵住 Redis 的接收线程，那是全进程共用的，
                    // 一个频道卡住，别的 Hub 的订阅也一起收不到消息
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

                _consumer = ConsumeAsync(handler);
            }

            /// <summary>Redis 接收线程上调用：只入队，立即返回</summary>
            internal void Post(KinnaKickCommand command)
            {
                if (_queue.Writer.TryWrite(command))
                {
                    return;
                }

                KinnaMetrics.RecordRemoteKickDropped(HubName);

                _logger.Warning(
                    $"[kinna] 跨副本踢人指令队列已满，已丢弃: subject={command.SubjectId} user={command.UserId}");
            }

            private async Task ConsumeAsync(Func<KinnaKickCommand, Task> handler)
            {
                while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (_queue.Reader.TryRead(out var command))
                    {
                        try
                        {
                            await handler(command).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // 一条失败不能退出循环：循环一停，之后所有踢人指令都收不到
                            _logger.Error(
                                $"[kinna] 处理跨副本踢人指令失败: subject={command.SubjectId} user={command.UserId}", ex);
                        }
                    }
                }
            }

            /// <summary>
            /// 退订。Redis 的 <c>Unsubscribe</c> 是按频道粒度的，同频道还有别的订阅者时
            /// 会一起断——本应用每个频道只挂这一个订阅，够用。
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                try
                {
                    _redis.Unsubscribe(KickChannel);
                }
                catch (Exception ex)
                {
                    // 停机路径：Redis 已经断开时退订失败不影响进程退出
                    _logger.Warning($"[kinna] 退订跨副本踢人频道失败: {ex.Message}");
                }

                // 先退订再封队列：退订之后不会再有新指令进来，队列里剩下的照常跑完。
                // 不等消费完成 —— 停机路径上多等一会儿就会拖慢滚动发布，
                // 剩下的指令随连接表一起消失，客户端靠租约过期兜底
                _queue.Writer.TryComplete();

                if (!_consumer.IsCompleted)
                {
                    _logger.Debug("[kinna] 退订时仍有踢人指令在途，剩余指令随停机丢弃");
                }
            }
        }
    }
}
