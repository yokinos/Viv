using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Viv.Aoi;
using Viv.Delusion;
using Viv.Log;
using Viv.Redis.DbAllocator;
using Viv.Delusion.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 连接管理器核心类
    /// 功能：
    /// 1. 支持单机/集群/哨兵三种Redis部署模式的连接配置与初始化；
    /// 2. 提供线程安全的连接实例获取、数据库实例获取能力；
    /// 3. 内置连接状态监控、异常日志记录、资源释放等能力；
    /// 4. 实现IDisposable接口，支持资源手动释放。
    /// </summary>
    public class RedisFactory : IDisposable
    {
        /// <summary>
        /// 配置初始化状态标识（线程安全）
        /// </summary>
        private static volatile bool _isConfigInitialized = false;

        /// <summary>
        /// 配置初始化锁
        /// </summary>
        private static readonly Lock _configLock = new();

        /// <summary>
        /// Redis配置选项
        /// </summary>
        public static RedisOptions? CurrentRedisOptions => VivConfigRegistry.Get<RedisOptions>();

        /// <summary>
        /// Db分配器
        /// </summary>
        protected IDbAllocator? _dbAllocator;

        /// <summary>
        /// 懒加载Redis连接实例（异步，线程安全）
        /// LazyThreadSafetyMode.ExecutionAndPublication：保证仅一次执行初始化逻辑
        /// </summary>
        private static readonly Lazy<Task<IConnectionMultiplexer>> _lazyAsyncConnection = new(GetConnectionMultiplexerAsync, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 资源释放状态标识
        /// </summary>
        private static bool _disposed = false;

        /// <summary>
        /// 分布式日志
        /// </summary>
        private static ILoggerContract? _logger;

        public RedisFactory()
        {
            ArgumentNullException.ThrowIfNull(CurrentRedisOptions);
            _dbAllocator = CurrentRedisOptions.SelectorType switch
            {
                DbSelectorType.KeyHash => new KeyHashAllocator(),
                DbSelectorType.TenantIdHash => new TenantIdAllocator(),
                DbSelectorType.None => new NoneAllocator(),
                _ => new NoneAllocator(),
            };

            _logger = VivLocator.GetAutofaService<ILoggerContract>();
        }

        /// <summary>
        /// 异步创建Redis连接实例（内部懒加载调用，无需外部调用）
        /// </summary>
        /// <returns>Redis连接多路复用器实例（IConnectionMultiplexer）</returns>
        /// <exception cref="InvalidOperationException">配置未初始化时抛出</exception>
        /// <exception cref="NotSupportedException">不支持的Redis部署模式时抛出</exception>
        private static async Task<IConnectionMultiplexer> GetConnectionMultiplexerAsync()
        {
            // 前置校验：配置必须已初始化
            if (!_isConfigInitialized || CurrentRedisOptions == null)
            {
                throw new InvalidOperationException("Redis配置未初始化！请先调用RedisFactory.Initialize方法");
            }

            // 根据部署模式构建配置
            var config = CurrentRedisOptions.RedisMode switch
            {
                RedisMode.Standalone => BuildStandaloneConfig(CurrentRedisOptions),
                RedisMode.Cluster => BuildClusterConfig(CurrentRedisOptions),
                RedisMode.Sentinel => BuildSentinelConfig(CurrentRedisOptions),
                _ => throw new NotSupportedException($"不支持的Redis部署模式: {CurrentRedisOptions.RedisMode}"),
            };

            var connection = await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
            RegisterConnectionEvents(connection);
            return connection;
        }

        /// <summary>
        /// 构建单机模式Redis配置
        /// </summary>
        /// <param name="options">Redis配置选项</param>
        /// <returns>单机模式配置实例</returns>
        private static ConfigurationOptions BuildStandaloneConfig(RedisOptions options)
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            SetCommonConfig(config, options);
            return config;
        }

        /// <summary>
        /// 构建集群模式Redis配置
        /// </summary>
        /// <param name="options">Redis配置选项</param>
        /// <returns>集群模式配置实例</returns>
        private static ConfigurationOptions BuildClusterConfig(RedisOptions options)
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            SetCommonConfig(config, options);
            return config;
        }

        /// <summary>
        /// 构建哨兵模式Redis配置
        /// </summary>
        /// <param name="options">Redis配置选项</param>
        /// <returns>哨兵模式配置实例</returns>
        /// <exception cref="ArgumentException">哨兵节点格式错误时抛出</exception>
        private static ConfigurationOptions BuildSentinelConfig(RedisOptions options)
        {
            var config = new ConfigurationOptions
            {
                CommandMap = CommandMap.Sentinel,
                TieBreaker = "",
                ServiceName = options.SentinelMasterName
            };

            SetCommonConfig(config, options);
            foreach (var endPoint in options.SentinelEndPoints ?? Enumerable.Empty<string>())
            {
                var parts = endPoint.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
                {
                    throw new ArgumentException($"哨兵节点格式错误: {endPoint}，正确格式：ip:port（如 127.0.0.1:26379）");
                }
                config.EndPoints.Add(parts[0], port);
            }

            WriteLog($"Redis哨兵配置构建完成，主节点名称：{options.SentinelMasterName}，哨兵节点数：{config.EndPoints.Count}");
            return config;
        }

        /// <summary>
        /// 设置Redis通用配置项（所有部署模式共享）
        /// </summary>
        /// <param name="config">配置实例</param>
        /// <param name="options">Redis配置选项</param>
        private static void SetCommonConfig(ConfigurationOptions config, RedisOptions options)
        {
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            config.AllowAdmin = options.AllowAdmin;
            config.AbortOnConnectFail = options.AbortOnConnectFail;
            config.DefaultDatabase = options.DefaultDatabase;
            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                config.Password = options.Password;
            }
            config.KeepAlive = options.KeepAlive;
        }

        /// <summary>
        /// 初始化Redis配置（线程安全，仅允许调用一次）
        /// </summary>
        /// <param name="options">Redis配置选项（包含部署模式、连接信息、超时等）</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出</exception>
        /// <exception cref="InvalidOperationException">重复初始化时抛出</exception>
        /// <exception cref="ArgumentException">配置必填项校验失败时抛出</exception>
        public static void Initialize(RedisOptions options)
        {
            if (!_isConfigInitialized)
            {
                lock (_configLock)
                {
                    if (!_isConfigInitialized)
                    {
                        ArgumentNullException.ThrowIfNull(options, nameof(options));
                        ValidateOptionsByMode(options);
                        VivConfigRegistry.Add(options);
                        _isConfigInitialized = true;
                        return;
                    }
                }
            }

            // 重复初始化抛出异常（保证配置唯一性）
            throw new InvalidOperationException("Redis配置已初始化，禁止重复调用Initialize方法");
        }

        /// <summary>
        /// 按部署模式校验配置必填项（内部校验逻辑）
        /// </summary>
        /// <param name="options">Redis配置选项</param>
        /// <exception cref="ArgumentException">必填项缺失时抛出</exception>
        /// <exception cref="NotSupportedException">不支持的Redis模式时抛出</exception>
        private static void ValidateOptionsByMode(RedisOptions options)
        {
            switch (options.RedisMode)
            {
                case RedisMode.Standalone:
                case RedisMode.Cluster:
                    if (options.ConnectionString.IsNullOrEmpty())
                    {
                        throw new Exception($"{options.RedisMode}模式下必须配置有效的连接字符串");
                    }
                    break;

                case RedisMode.Sentinel:
                    if (options.SentinelEndPoints.IsNullOrEmpty())
                    {
                        throw new Exception("哨兵模式下必须配置至少一个哨兵节点");
                    }
                    if (options.SentinelMasterName.IsNullOrEmpty())
                    {
                        throw new Exception("哨兵模式下必须配置主节点名称");
                    }
                    break;

                default:
                    throw new NotSupportedException($"不支持的Redis模式: {options.RedisMode}");
            }
        }

        /// <summary>
        /// 注册Redis连接事件（监控连接状态，仅StackExchange.Redis 2.10.1版本支持）
        /// 监控事件：连接失败、连接恢复、错误消息、配置变更、哈希槽移动、内部错误
        /// </summary>
        /// <param name="connection">Redis连接实例</param>
        private static void RegisterConnectionEvents(IConnectionMultiplexer connection)
        {
            connection.ConnectionFailed += (sender, args) =>
            {
                WriteLog($"Redis连接失败: {args.Exception?.Message}, Endpoint: {args.EndPoint}, FailureType: {args.FailureType}");
            };
            connection.ConnectionRestored += (sender, args) =>
            {
                WriteLog($"Redis连接已恢复, Endpoint: {args.EndPoint}, FailureType: {args.FailureType}");
            };
            connection.ErrorMessage += (sender, args) =>
            {
                WriteLog($"Redis错误消息: {args.Message}");
            };
            connection.ConfigurationChanged += (sender, args) =>
            {
                WriteLog("Redis配置已更改");
            };
            connection.HashSlotMoved += (sender, args) =>
            {
                WriteLog($"Redis哈希槽已移动: NewEndPoint: {args.NewEndPoint}, OldEndPoint: {args.OldEndPoint}");
            };
            connection.InternalError += (sender, args) =>
            {
                WriteLog($"Redis内部错误: {args.Exception?.Message}", args.Exception);
            };
        }

        /// <summary>
        /// 日志输出封装方法（便于后续替换日志框架，统一日志格式）
        /// </summary>
        /// <param name="message">日志消息内容</param>
        /// <param name="ex">异常实例（可选，无异常时传null）</param>
        protected static void WriteLog(string message, Exception? ex = null)
        {
            if (ex == null)
            {
                _logger?.Error(message);
            }
            else
            {
                _logger?.Error(message, ex);
            }
        }

        /// <summary>
        /// 获取Redis连接实例（异步，懒加载）
        /// 首次调用时初始化连接，后续调用复用已有连接
        /// </summary>
        /// <returns>Redis连接多路复用器实例（IConnectionMultiplexer）</returns>
        /// <exception cref="InvalidOperationException">配置未初始化时抛出</exception>
        public static Task<IConnectionMultiplexer> GetConnectionAsync()
        {
            if (!_isConfigInitialized)
            {
                throw new InvalidOperationException("Redis配置未初始化！请先调用Initialize方法");
            }
            return _lazyAsyncConnection.Value;
        }

        /// <summary>
        /// 根据Key路由获取Redis数据库实例（异步）
        /// 核心逻辑：通过RedisMagic.AllocateDbIndex(key)计算Key所属的数据库编号
        /// </summary>
        /// <param name="key">Redis键（用于路由到对应数据库）</param>
        /// <returns>指定数据库的操作实例（IDatabase）</returns>
        public async Task<IDatabase> GetDatabaseAsync(string key)
        {
            var dbIndex = _dbAllocator?.AllocateDbIndex(key, CurrentRedisOptions?.MaxDbIndex);
            return await GetDatabaseAsync(dbIndex).ConfigureAwait(false);
        }

        /// <summary>
        /// 获取Redis数据库实例（同步）
        /// 内部通过异步转同步实现，注意：可能造成线程阻塞，高频场景建议使用异步版本
        /// </summary>
        /// <param name="dbNumber">数据库编号（null则使用配置默认值）</param>
        /// <returns>指定数据库的操作实例（IDatabase）</returns>
        public static IDatabase GetDatabase(int? dbNumber = null)
        {
            // 改用 GetAwaiter().GetResult() 避免 Task.Run 死锁风险
            return GetDatabaseAsync(dbNumber).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取Redis数据库实例（异步）
        /// 特殊规则：集群模式下强制使用0号库（Redis集群不支持多数据库）
        /// </summary>
        /// <param name="dbNumber">数据库编号（null则使用配置默认值）</param>
        /// <returns>指定数据库的操作实例（IDatabase）</returns>
        public async static Task<IDatabase> GetDatabaseAsync(int? dbNumber = null)
        {
            if (CurrentRedisOptions?.RedisMode == RedisMode.Cluster)
            {
                // 集群模式下只能使用0号库
                dbNumber = 0;
            }

            var connection = await GetConnectionAsync().ConfigureAwait(false);
            return connection.GetDatabase(dbNumber ?? (CurrentRedisOptions?.DefaultDatabase ?? 0));
        }

        /// <summary>
        /// 获取Redis服务器实例（异步）
        /// 用于执行服务器级操作（如配置、统计、键遍历等）
        /// </summary>
        /// <param name="endPoint">服务器端点（null则返回第一个可用节点）</param>
        /// <returns>Redis服务器操作实例（IServer）</returns>
        /// <exception cref="InvalidOperationException">未找到可用服务器端点时抛出</exception>
        public async static Task<IServer> GetServerAsync(EndPoint? endPoint = null)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var endPoints = connection.GetEndPoints();

            if (endPoints.Length == 0)
            {
                throw new InvalidOperationException("未找到可用的Redis服务器端点");
            }

            return connection.GetServer(endPoint ?? endPoints.First());
        }

        /// <summary>
        /// 释放Redis连接资源（实现IDisposable接口）
        /// 建议：应用程序退出时调用，释放连接多路复用器资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 资源释放核心逻辑（受保护的虚方法，支持子类重写）
        /// </summary>
        /// <param name="disposing">true=手动释放托管资源，false=仅释放非托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // Redis 连接是进程级共享单例（static Lazy<IConnectionMultiplexer>，设计上存活整个进程）。
                // 由任一实例 Dispose 关闭它/重置 static 状态（_isConfigInitialized/VivConfigRegistry）会永久砖掉
                // 整个进程的缓存通道：Lazy 已创建无法重建，static 状态被污染，后续所有 Redis 操作全部失败。
                // 连接生命周期跟随进程，应用退出时由进程回收即可，这里不做任何关闭。
                WriteLog("RedisFactory.Dispose：Redis 连接为进程级共享，跳过关闭");
            }

            _disposed = true;
        }
    }
}