using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 跨副本踢人的订阅端。每个副本订同一个频道，收到指令只踢本机连接，
    /// 不需要知道对方在哪台机器上。
    ///
    /// 没有注册 <see cref="IKinnaKickBus{THub}"/> 时整体空转：单副本部署、
    /// 或者宿主没接 Redis 的部署照常跑，踢人退化成只作用于本副本（和以前一致）。
    ///
    /// 这里只调 <see cref="IKinnaConnectionCoordinator{THub}.KickUserAsync"/> /
    /// <see cref="IKinnaConnectionCoordinator{THub}.KickClientAsync"/> /
    /// <see cref="IKinnaConnectionCoordinator{THub}.KickSubjectAsync"/> 这三个**本地**方法。
    /// 调用 *Everywhere* 会再广播一次，自己收自己的消息就成环了。
    /// </summary>
    public sealed class KinnaKickSubscriber<THub> : BackgroundService where THub : Hub
    {
        private static readonly string HubName = typeof(THub).Name;

        private readonly IKinnaKickBus<THub>? _bus;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILoggerContract _logger;

        // StartAsync 返回时 ExecuteAsync 未必已经跑到订阅那一步（.NET 10 的
        // BackgroundService 不再保证同步），而停机时 Stop/Dispose 可能紧跟着来。
        // 不锁住的话「订阅」可能落在 Dispose 之后，那份订阅再没人退，
        // 回调会一直打到已释放的 scope 上
        private readonly object _gate = new();

        private IDisposable? _subscription;
        private bool _disposed;
        private CancellationToken _stopping = CancellationToken.None;

        public KinnaKickSubscriber(
            IServiceScopeFactory scopes,
            ILoggerContract logger,
            IKinnaKickBus<THub>? bus = null)
        {
            _scopes = scopes;
            _logger = logger;
            _bus = bus;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_bus == null)
            {
                _logger.Debug($"[{HubName}] 未注册 IKinnaKickBus<{HubName}>，跨副本踢人未启用");
                return Task.CompletedTask;
            }

            _stopping = stoppingToken;

            lock (_gate)
            {
                // 已经被停掉/释放了就别再订：订了没人退
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                _subscription = _bus.Subscribe(DispatchAsync);
            }

            _logger.Info($"[{HubName}] 跨副本踢人已订阅，本实例 {_bus.InstanceId}");

            // 订阅是常驻回调，没有循环要跑。返回完成的任务：宿主不会重启它，
            // 真正的回收在 Dispose 里退订
            return Task.CompletedTask;
        }

        private async Task DispatchAsync(KinnaKickCommand command)
        {
            // 脏消息一律丢弃：UserId 为 0 表示整租户，SubjectId 都没有就没法定位
            if (command.SubjectId <= 0)
            {
                _logger.Warning($"[{HubName}] 收到无效的踢人指令，已丢弃: subject={command.SubjectId} user={command.UserId}");
                return;
            }

            // 发布方先踢过本机了，跳过自己那份
            if (!string.IsNullOrEmpty(command.Origin) && command.Origin == _bus?.InstanceId)
            {
                return;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();

                // 三种粒度，靠 UserId / AppId 是否为 0 区分：
                // 整租户（UserId=0）→ 某用户全部客户端（AppId=0）→ 某用户某一端
                if (command.UserId <= 0)
                {
                    await coordinator.KickSubjectAsync(command.SubjectId, _stopping).ConfigureAwait(false);
                }
                else if (command.AppId > 0)
                {
                    await coordinator
                        .KickClientAsync(command.AppId, command.SubjectId, command.UserId, _stopping)
                        .ConfigureAwait(false);
                }
                else
                {
                    await coordinator.KickUserAsync(command.SubjectId, command.UserId, _stopping).ConfigureAwait(false);
                }

                KinnaMetrics.RecordRemoteKick(HubName);

                _logger.Info(
                    $"[{HubName}] 已执行跨副本踢人: subject={command.SubjectId} user={command.UserId} " +
                    $"app={command.AppId} 来源={command.Origin ?? "-"} 原因={command.Reason ?? "-"}");
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                // 停机中，剩下没踢的随连接表一起消失
            }
            catch (Exception ex)
            {
                // 回调在 Redis 的接收线程上，异常抛出去没人接，整个订阅会静默失效
                _logger.Error(
                    $"[{HubName}] 执行跨副本踢人失败: subject={command.SubjectId} user={command.UserId}", ex);
            }
        }

        public override void Dispose()
        {
            IDisposable? subscription;

            lock (_gate)
            {
                _disposed = true;
                subscription = _subscription;
                _subscription = null;
            }

            subscription?.Dispose();
            base.Dispose();
        }
    }
}
