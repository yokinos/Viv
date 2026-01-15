using StackExchange.Redis;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using Viv.Shared;

namespace Viv.Cache.Redis
{
    /// <summary>
    /// Redis连接管理器
    /// </summary>
    public class RedisFactory
    {
        private static RedisOptions? _redisOptions;
        private static bool _isConfigInitialized = false;
        private static readonly Lock _configLock = new();
        private static readonly Lazy<Task<IConnectionMultiplexer>> _lazyAsyncConnection = new(() => GetConnectionMultiplexerAsync());

        private static async Task<IConnectionMultiplexer> GetConnectionMultiplexerAsync()
        {
            if (!_isConfigInitialized || _redisOptions == null)
            {
                throw new InvalidOperationException("Redis配置未初始化！请先调用初始化方法扩展方法");
            }

            ConfigurationOptions config;

            // 1. 哨兵模式处理
            if (_redisOptions.IsSentinelMode)
            {
                config = new ConfigurationOptions
                {
                    CommandMap = CommandMap.Sentinel,
                    TieBreaker = "",
                    AbortOnConnectFail = _redisOptions.AbortOnConnectFail,
                    AllowAdmin = _redisOptions.AllowAdmin,
                    ConnectTimeout = _redisOptions.ConnectTimeout,
                    SyncTimeout = _redisOptions.SyncTimeout,
                    DefaultDatabase = _redisOptions.DefaultDatabase,
                    Password = _redisOptions.Password
                };

                foreach (var endPoint in _redisOptions.SentinelEndPoints)
                {
                    var parts = endPoint.Split(':');
                    if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
                        throw new ArgumentException($"哨兵节点格式错误: {endPoint}，正确格式：ip:port");

                    config.EndPoints.Add(parts[0], port);
                }

                config.ServiceName = _redisOptions.SentinelMasterName;
            }
            else
            {
                config = ConfigurationOptions.Parse(_redisOptions.ConnectionString);
                config.ConnectTimeout = _redisOptions.ConnectTimeout;
                config.SyncTimeout = _redisOptions.SyncTimeout;
                config.AllowAdmin = _redisOptions.AllowAdmin;
                config.AbortOnConnectFail = _redisOptions.AbortOnConnectFail;
                if (!string.IsNullOrWhiteSpace(_redisOptions.Password))
                    config.Password = _redisOptions.Password;
                if (_redisOptions.DefaultDatabase != 0)
                    config.DefaultDatabase = _redisOptions.DefaultDatabase;
            }

            // 创建连接（自动适配模式）
            var connection = await ConnectionMultiplexer.ConnectAsync(config);
            RegisterConnectionEvents(connection);
            return connection;
        }

        /// <summary>
        /// 初始化 Redis 配置（线程安全）
        /// </summary>
        /// <param name="options">Redis 配置选项</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出</exception>
        public static void Initialize(RedisOptions options)
        {
            lock (_configLock)
            {
                ArgumentNullException.ThrowIfNull(options, nameof(RedisOptions));

                // 校验不同模式的必填项
                if (options.IsSentinelMode)
                {
                    if (options.SentinelEndPoints == null || !options.SentinelEndPoints.Any())
                        throw new ArgumentException("哨兵模式下必须配置哨兵节点列表", nameof(options));
                    if (string.IsNullOrWhiteSpace(options.SentinelMasterName))
                        throw new ArgumentException("哨兵模式下必须配置主节点名称", nameof(options));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(options.ConnectionString))
                        throw new ArgumentException("非哨兵模式下必须配置连接字符串", nameof(options));
                }

                _redisOptions = options;
                _isConfigInitialized = true;
            }
        }

        /// <summary>
        /// 注册 Redis 连接事件（监控连接状态）
        /// </summary>
        private static void RegisterConnectionEvents(IConnectionMultiplexer connection)
        {
            // 连接配置变更事件
            connection.ConfigurationChanged += (sender, args) => WriteLog($"Redis 配置变更: {args.EndPoint}");
            // 连接配置变更（异步）事件
            connection.ConfigurationChangedBroadcast += (sender, args) => WriteLog($"Redis 配置变更广播: {args.EndPoint}");
            // 连接断开事件
            connection.ConnectionFailed += (sender, args) => WriteLog($"Redis 连接失败: {args?.Exception?.Message}, 端点: {args?.EndPoint}");
            // 连接重新建立事件
            connection.ConnectionRestored += (sender, args) => WriteLog($"Redis 连接恢复: {args.EndPoint}");
            // 集群节点移动事件（仅集群模式生效）
            connection.HashSlotMoved += (sender, args) => WriteLog($"Redis 哈希槽移动: 从 {args.OldEndPoint} 到 {args.NewEndPoint}");
            // 内部错误事件
            connection.InternalError += (sender, args) => WriteLog($"Redis 内部错误: {args.Exception.Message}");
        }

        protected static void WriteLog(string message)
        {
            Console.Write(message);
        }

        public Task<IConnectionMultiplexer> GetConnectionAsync()
        {
            return _lazyAsyncConnection.Value;
        }

        [return: MaybeNull]
        public async Task<T?> TryExecute<T>(Func<IDatabase, Task<T>> func)
        {
            try
            {
                var database = await GetDatabaseAsync();
                return await func.Invoke(database);
            }
            catch (Exception ex)
            {
                VivLogger.Error(ex);
                return default;
            }
        }

        public async Task<IDatabase> GetDatabaseAsync(int? dbNumber = null)
        {
            var connection = await GetConnectionAsync();
            return connection.GetDatabase(dbNumber ?? (_redisOptions?.DefaultDatabase ?? 0));
        }

        /// <summary>
        /// 获取 Redis 服务器实例（集群/哨兵模式下返回所有节点）
        /// </summary>
        /// <param name="endPoint">服务器端点（null 表示第一个可用节点）</param>
        /// <returns>Redis 服务器实例</returns>
        public async Task<IServer> GetServerAsync(EndPoint? endPoint = null)
        {
            var connection = await GetConnectionAsync();
            return endPoint == null ? connection.GetServer(connection.GetEndPoints().First()) : connection.GetServer(endPoint);
        }
    }
}