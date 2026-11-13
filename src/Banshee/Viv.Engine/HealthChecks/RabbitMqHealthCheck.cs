using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Viv.Nana.Options;

namespace Viv.Engine.HealthChecks
{
    /// <summary>
    /// RabbitMQ 连通性检查：建一条真连接、握手成功才判健康，用完立刻关。
    ///
    /// 不复用 Wolverine 手里那条连接：它在 <c>Wolverine.RabbitMQ.Internal</c> 里，是包内部实现，
    /// 升级就断；而且「连接对象在不在」也不等于「broker 通」—— 和 Redis 那条同理，判据必须是真往返。
    ///
    /// 每次探测新建一条短连接是刻意的：调用频率由编排系统决定（通常几十秒一次），
    /// 比缓存一条再读 IsOpen 更准 —— 缓存的那条自己会重连，重连窗口里 IsOpen 是 false
    /// 但 broker 其实好好的，等于把正常的抖动报成故障。
    ///
    /// 打 ready 不打 live：MQ 挂了该摘流量，不该让编排系统去重启进程。
    /// </summary>
    public sealed class RabbitMqHealthCheck : IHealthCheck
    {
        /// <summary>连接超时。给 3 秒是为了别让 /health 卡在一条连不上的 TCP 上。</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        private readonly Uri _uri;

        public RabbitMqHealthCheck(NanaOptions options)
        {
            _uri = BuildUri(options);
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var factory = new ConnectionFactory
            {
                Uri = _uri,
                RequestedConnectionTimeout = Timeout,
                AutomaticRecoveryEnabled = false
            };

            IConnection? connection = null;
            try
            {
                connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

                return connection.IsOpen
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("RabbitMQ 连接未打开");
            }
            catch (Exception ex)
            {
                // 和 Redis / Database 两条一致：健康检查负责说结论，不往外抛 ——
                // 抛出去整个 /health 变 500，反而看不出是哪一项挂了
                return HealthCheckResult.Unhealthy("RabbitMQ 不可达", ex);
            }
            finally
            {
                await DisposeQuietlyAsync(connection).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 收尾失败不影响结论：探测的意义是「现在通不通」，关连接出错说明不了什么。
        ///
        /// 按能力探测而不是绑定具体接口：RabbitMQ.Client 各版本之间 IDisposable / IAsyncDisposable
        /// 的归属变过，写死一个会在升级时编译不过。
        /// </summary>
        private static async Task DisposeQuietlyAsync(IConnection? connection)
        {
            if (connection is null) return;

            try
            {
                if (connection is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (connection is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
                // 见方法注释：here be no dragons worth reporting
            }
        }

        /// <summary>
        /// 与 <c>VivWolverineConfigurationExtensions</c> 拼 URI 的规则保持一致：用户名/密码要转义
        /// （密码里带 @ 或 : 是常见配置），vhost 去掉前导斜杠。两边规则一旦不一致，
        /// 会出现「Wolverine 连得上、健康检查连不上」的假红。
        /// </summary>
        private static Uri BuildUri(NanaOptions options)
        {
            var vhost = options.VirtualHost.TrimStart('/');
            return new Uri(
                $"amqp://{Uri.EscapeDataString(options.UserName)}:{Uri.EscapeDataString(options.Password)}" +
                $"@{options.Host}:{options.Port}/{vhost}");
        }
    }
}
