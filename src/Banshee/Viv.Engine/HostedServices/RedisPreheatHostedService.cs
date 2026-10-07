using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Redis;

namespace Viv.Engine.HostedServices
{
    /// <summary>
    /// Redis 预热：启动时解析一次 <see cref="IRedisService"/>，让 <c>RedisFactory.Initialize</c> 真的跑一遍。
    ///
    /// 起因：/health 的 <see cref="HealthChecks.RedisHealthCheck"/> 直接调静态 <c>RedisFactory.GetConnectionAsync()</c>，
    /// 而 Initialize 只在有人解析过 IRedisService 时才执行 —— 进程刚起、还没人碰过缓存的那段时间里，
    /// 健康检查拿到的是"配置未初始化"这个假故障，/health 一开机就报 Redis 不可达。
    ///
    /// 接在 RegisterCache 里（Redis 被选为缓存提供者时才注册）：那里正是 IRedisService 的注册点，
    /// 该开 Redis 的宿主一定会走到，和 /health 挂 redis 检查的条件也是同一个。
    ///
    /// 拿不到 Redis 一律只记日志：Redis 只是配置传播的加速器，它不可用不该让宿主起不来，
    /// 结论由健康检查自己说。真连一次放后台做，免得连接超时把启动卡住。
    /// </summary>
    public sealed class RedisPreheatHostedService : IHostedService
    {
        private readonly IServiceProvider _provider;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="provider">根容器：预热要在宿主启动期就能解析 IRedisService</param>
        public RedisPreheatHostedService(IServiceProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// 启动时把 Redis 配置状态备好；任何异常都吞掉
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 日志是可选依赖：没配 LogOption 的宿主根本没有 ILoggerContract，这里不能因此起不来
            var logger = _provider.GetService(typeof(ILoggerContract)) as ILoggerContract;

            try
            {
                if (_provider.GetService(typeof(IRedisService)) is null)
                {
                    logger?.Info("未注册 IRedisService，跳过 Redis 预热");
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                logger?.Warning("Redis 预热失败（配置非法或依赖缺失），/health 的 redis 项会如实报出来：{0}", ex.Message);
                return Task.CompletedTask;
            }

            _ = WarmUpAsync(logger);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 无需释放
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// 把连接建起来并 PING 一次，异常只记日志 —— 观测不到就退化成"第一次用的时候再连"
        /// </summary>
        /// <param name="logger">可选日志</param>
        private static async Task WarmUpAsync(ILoggerContract? logger)
        {
            try
            {
                var connection = await RedisFactory.GetConnectionAsync().ConfigureAwait(false);
                await connection.GetDatabase().PingAsync().ConfigureAwait(false);
                logger?.Info("Redis 预热完成");
            }
            catch (Exception ex)
            {
                logger?.Warning("Redis 预热连接失败，/health 的 redis 项会如实报出来：{0}", ex.Message);
            }
        }
    }
}
