using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Viv.Log;
using Viv.Vva.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// Redis连接管理器
    /// </summary>
    public class RedisFactory : IDisposable
    {
        private static volatile bool _isConfigInitialized = false;
        private static readonly Lock _configLock = new();
        private static RedisOptions? _redisOptions;
        private static readonly Lazy<Task<IConnectionMultiplexer>> _lazyAsyncConnection = new(GetConnectionMultiplexerAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        private static bool _disposed = false;

        private static async Task<IConnectionMultiplexer> GetConnectionMultiplexerAsync()
        {
            // 前置校验：配置必须已初始化
            if (!_isConfigInitialized || _redisOptions == null)
            {
                throw new InvalidOperationException("Redis配置未初始化！请先调用RedisFactory.Initialize方法");
            }

            // 根据部署模式构建配置
            var config = _redisOptions.RedisMode switch
            {
                RedisMode.Standalone => BuildStandaloneConfig(_redisOptions),
                RedisMode.Cluster => BuildClusterConfig(_redisOptions),
                RedisMode.Sentinel => BuildSentinelConfig(_redisOptions),
                _ => throw new NotSupportedException($"不支持的Redis部署模式: {_redisOptions.RedisMode}"),
            };

            var connection = await ConnectionMultiplexer.ConnectAsync(config);
            RegisterConnectionEvents(connection);
            return connection;
        }

        private static ConfigurationOptions BuildStandaloneConfig(RedisOptions options)
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            SetCommonConfig(config, options);
            return config;
        }

        private static ConfigurationOptions BuildClusterConfig(RedisOptions options)
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            SetCommonConfig(config, options);
            return config;
        }

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
        /// <param name="options">Redis配置选项</param>
        /// <exception cref="ArgumentNullException">配置为空时抛出</exception>
        /// <exception cref="InvalidOperationException">重复初始化时抛出</exception>
        /// <exception cref="ArgumentException">配置校验失败时抛出</exception>
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
                        _redisOptions = options.DeepCopy();
                        _isConfigInitialized = true;
                        return;
                    }
                }
            }

            // 重复初始化抛出异常（保证配置唯一性）
            throw new InvalidOperationException("Redis配置已初始化，禁止重复调用Initialize方法");
        }

        /// <summary>
        /// 按部署模式校验配置必填项
        /// </summary>
        private static void ValidateOptionsByMode(RedisOptions options)
        {
            switch (options.RedisMode)
            {
                case RedisMode.Standalone:
                case RedisMode.Cluster:
                    if (string.IsNullOrWhiteSpace(options.ConnectionString))
                    {
                        throw new ArgumentException($"{options.RedisMode}模式下必须配置有效的连接字符串", nameof(options.ConnectionString));
                    }
                    break;

                case RedisMode.Sentinel:
                    if (options.SentinelEndPoints == null || !options.SentinelEndPoints.Any())
                    {
                        throw new ArgumentException("哨兵模式下必须配置至少一个哨兵节点", nameof(options.SentinelEndPoints));
                    }
                    if (string.IsNullOrWhiteSpace(options.SentinelMasterName))
                    {
                        throw new ArgumentException("哨兵模式下必须配置主节点名称", nameof(options.SentinelMasterName));
                    }
                    break;

                default:
                    throw new NotSupportedException($"不支持的Redis模式: {options.RedisMode}");
            }
        }

        /// <summary>
        /// 注册Redis连接事件（监控连接状态，纯2.10.1版本支持）
        /// </summary>
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
        /// 日志输出（封装，便于后续替换日志框架）
        /// </summary>
        private static void WriteLog(string message, Exception? ex = null)
        {
            if (ex == null)
            {
                WriteLogger.Error(message);
            }
            else
            {
                WriteLogger.Error(message, ex);
            }
        }

        /// <summary>
        /// 获取Redis连接实例（异步，懒加载）
        /// </summary>
        public static Task<IConnectionMultiplexer> GetConnectionAsync()
        {
            if (!_isConfigInitialized)
            {
                throw new InvalidOperationException("Redis配置未初始化！请先调用Initialize方法");
            }
            return _lazyAsyncConnection.Value;
        }

        public static async Task<IDatabase> GetDatabaseAsync(string key)
        {
            var dbIndex = RedisMagic.AllocateDbIndex(key);
            return await GetDatabaseAsync(dbIndex);
        }

        /// <summary>
        /// 获取Redis数据库实例（异步）
        /// </summary>
        /// <param name="dbNumber">数据库编号（null则使用配置默认值）</param>
        public static async Task<IDatabase> GetDatabaseAsync(int? dbNumber = null)
        {
            if (_redisOptions?.RedisMode == RedisMode.Cluster)
            {
                // 集群模式下只能使用0号库
                dbNumber = 0;
            }

            var connection = await GetConnectionAsync();
            return connection.GetDatabase(dbNumber ?? (_redisOptions?.DefaultDatabase ?? 0));
        }

        /// <summary>
        /// 获取Redis服务器实例（异步）
        /// </summary>
        /// <param name="endPoint">服务器端点（null则返回第一个可用节点）</param>
        public static async Task<IServer> GetServerAsync(EndPoint? endPoint = null)
        {
            var connection = await GetConnectionAsync();
            var endPoints = connection.GetEndPoints();

            if (endPoints.Length == 0)
            {
                throw new InvalidOperationException("未找到可用的Redis服务器端点");
            }

            return connection.GetServer(endPoint ?? endPoints.First());
        }

        /// <summary>
        /// 安全执行Redis操作（带异常捕获）
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="func">Redis操作委托</param>
        /// <returns>操作结果（失败返回默认值）</returns>
        [return: MaybeNull]
        public static async Task<T?> TryExecuteAsync<T>(string key, Func<IDatabase, Task<T>> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = await GetDatabaseAsync(key);
                return await func(database);
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return default;
            }
        }

        /// <summary>
        /// 同步执行Redis操作
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="func">Redis操作委托</param>
        /// <returns>操作结果（失败返回默认值）</returns>
        [return: MaybeNull]
        public static T? TryExecute<T>(string key, Func<IDatabase, T> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = Task.Run(async () => await GetDatabaseAsync(key)).Result;
                return func(database);
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return default;
            }
        }

        /// <summary>
        /// 释放Redis连接资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                if (_lazyAsyncConnection.IsValueCreated)
                {
                    var connectionTask = _lazyAsyncConnection.Value;
                    if (connectionTask.IsCompletedSuccessfully)
                    {
                        var connection = connectionTask.Result;
                        connection.Close();
                        connection.Dispose();
                    }
                }

                _isConfigInitialized = false;
                _redisOptions = null;
            }

            _disposed = true;
        }
    }
}