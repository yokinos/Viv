using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Viv.Redis;

namespace Viv.Engine.HealthChecks
{
    /// <summary>
    /// Redis 连通性检查。探的是真往返（PING），不是「连接对象还在不在」——
    /// 连接对象在连接断了之后照样拿得到，拿它当判据等于永远绿。
    ///
    /// 没有构造参数：RedisFactory.GetConnectionAsync 是静态的，AddCheck 靠 ActivatorUtilities
    /// 就能建出实例，不必注册进 DI。这一点正好躲开 AddCheck 那个坑 —— 它是在根 provider 上
    /// 建实例的，注入 Scoped 服务会拿到跨请求共享的那一份。
    ///
    /// 打 ready 不打 live：进程活着就是活着，Redis 挂了不该让编排系统去重启它。
    /// </summary>
    public sealed class RedisHealthCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var connection = await RedisFactory.GetConnectionAsync().ConfigureAwait(false);
                await connection.GetDatabase().PingAsync().ConfigureAwait(false);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                // 配置没初始化、连不上、Ping 超时都从这儿出去。健康检查的角色就是「说结论」，
                // 所以不往外抛 —— 抛出去会让整个 /health 变成 500，反而看不出是哪一项挂了
                return HealthCheckResult.Unhealthy("Redis 不可达", ex);
            }
        }
    }
}
