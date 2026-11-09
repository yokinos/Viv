using StackExchange.Redis;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Contracts;
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
            // Origin 由通道填而不是协调器填：协调器不该知道「我是哪个实例」
            command.Origin ??= InstanceId;

            await _redis.PublishAsync(KickChannel, command).ConfigureAwait(false);
        }

        public IDisposable Subscribe(Func<KinnaKickCommand, Task> handler)
        {
            // 回调跑在 Redis 的接收线程上：既不能阻塞，也不能把异常抛回去——
            // 抛出去没人接，整个订阅会静默失效，之后所有踢人指令都收不到
            _redis.Subscribe<KinnaKickCommand>(KickChannel, command =>
            {
                if (command == null)
                {
                    return;
                }

                _ = RunAsync(command);
            });

            return new Subscription(_redis, _logger);

            async Task RunAsync(KinnaKickCommand received)
            {
                try
                {
                    await handler(received).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[kinna] 处理跨副本踢人指令失败: subject={received.SubjectId} user={received.UserId}", ex);
                }
            }
        }

        /// <summary>
        /// 退订。Redis 的 <c>Unsubscribe</c> 是按频道粒度的，同频道还有别的订阅者时
        /// 会一起断——本应用每个频道只挂这一个订阅，够用。
        /// </summary>
        private sealed class Subscription : IDisposable
        {
            private readonly IRedisService _redis;
            private readonly ILoggerContract _logger;
            private int _disposed;

            internal Subscription(IRedisService redis, ILoggerContract logger)
            {
                _redis = redis;
                _logger = logger;
            }

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
            }
        }
    }
}
