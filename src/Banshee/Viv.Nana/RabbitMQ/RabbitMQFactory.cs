using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Log;
using Viv.Log.VivLogger;
using Viv.Nana.Options;
using Viv.Vva;

namespace Viv.Nana.RabbitMq
{
    /// <summary>
    /// RabbitMQ连接工厂
    /// </summary>
    public class RabbitMQFactory 
    {
        protected readonly Lock _connectionLock = new();
        protected IConnection? _connection;
        protected readonly RabbitMQOptions? _options;
        protected readonly IVivLogger _logger;

        public RabbitMQFactory()
        {
            _options = VivConfigRegistry.Get<RabbitMQOptions>();
            ArgumentNullException.ThrowIfNull(_options, "RabbitMQ配置未加载（VivConfigRegistry中未找到RabbitMQOptions）");
            ValidateOptions(_options);
            _logger = VivLogFactory.GetLogger();
        }

        /// <summary>
        /// 获取MQ连接（无连接则创建，连接失效则重建）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>有效MQ连接</returns>
        /// <exception cref="VivConnectionException">连接失败抛出框架统一异常</exception>
        public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            // 双重检查锁：保证线程安全，避免重复创建连接
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }

            lock (_connectionLock)
            {
                if (_connection != null && _connection.IsOpen)
                {
                    return _connection;
                }

                // 关闭失效连接（避免资源泄露）
                _connection?.Dispose();
                _connection = null;
            }

            // 创建新连接
            return await CreateConnectionWithRetryAsync(cancellationToken);
        }

        /// <summary>
        /// 创建MQ连接
        /// </summary>
        private async Task<IConnection> CreateConnectionWithRetryAsync(CancellationToken cancellationToken, int retryCount = 3)
        {
            ArgumentNullException.ThrowIfNull(_options);
            var retryDelay = TimeSpan.FromSeconds(2);
            var resourceAddress = $"{_options.HostName}:{_options.Port}/{_options.VirtualHost}";

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    var factory = new ConnectionFactory()
                    {
                        // 连接恢复配置（生产级必备）
                        AutomaticRecoveryEnabled = true,    // 连接断开自动恢复
                        TopologyRecoveryEnabled = true,     // 拓扑（队列/交换机）自动恢复
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5), // 恢复间隔
                        RequestedConnectionTimeout = TimeSpan.FromSeconds(60), // 连接超时
                        RequestedHeartbeat = TimeSpan.FromSeconds(30),       // 心跳检测
                        ContinuationTimeout = TimeSpan.FromSeconds(10),       // 操作超时

                        // 业务配置
                        HostName = _options.HostName,
                        Port = _options.Port,
                        UserName = _options.UserName,
                        Password = _options.Password,
                        VirtualHost = _options.VirtualHost,
                    };

                    // 异步创建连接
                    var connection = await factory.CreateConnectionAsync(cancellationToken);

                    // 注册连接关闭事件（便于排查断开原因）
                    connection.ConnectionShutdownAsync += async (sender, e) =>
                    {
                        _logger.Warn($"【RabbitMQ连接】连接已关闭，地址：{resourceAddress}，原因：{e.ReplyText}，错误码：{e.ReplyCode}");
                    };

                    _connection = connection;
                    return connection;
                }
                catch (Exception ex) when (i < retryCount - 1)
                {
                    _logger.Warn($"【RabbitMQ连接】第{i + 1}次创建失败，地址：{resourceAddress}，原因：{ex.Message}，将在{retryDelay.TotalSeconds}秒后重试");
                    await Task.Delay(retryDelay, cancellationToken);
                }
                catch (Exception ex)
                {
                    var errorMsg = $"RabbitMQ连接创建失败（已重试{retryCount}次），地址：{resourceAddress}";
                    _logger.Error(errorMsg, ex);

                    throw new VivConnectionException(VivConnType.RabbitMQ, resourceAddress, errorMsg, ex);
                }
            }

            // 理论上不会走到这里（最后一次重试会抛异常）
            throw new VivConnectionException(VivConnType.RabbitMQ, "RabbitMQ连接创建失败：未知错误");
        }

        /// <summary>
        /// 校验MQ配置有效性
        /// </summary>
        private void ValidateOptions(RabbitMQOptions options)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(options.HostName))
                errors.Add("MQ主机地址（HostName）不能为空");

            if (options.Port <= 0 || options.Port > 65535)
                errors.Add($"MQ端口（Port）无效：{options.Port}，必须是1-65535之间的整数");

            if (string.IsNullOrWhiteSpace(options.UserName))
                errors.Add("MQ用户名（UserName）不能为空");

            if (string.IsNullOrWhiteSpace(options.Password))
                errors.Add("MQ密码（Password）不能为空");

            if (string.IsNullOrWhiteSpace(options.VirtualHost))
                errors.Add("MQ虚拟主机（VirtualHost）不能为空");

            if (errors.Count > 0)
            {
                var errorMsg = $"RabbitMQ配置无效：{string.Join("；", errors)}";
                _logger?.Error(errorMsg);
                throw new ArgumentException(errorMsg, nameof(options));
            }
        }
    }
}