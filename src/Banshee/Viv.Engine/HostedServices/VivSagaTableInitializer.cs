using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Log;
using Viv.Nana.Options;
using Viv.Nana.Saga;

namespace Viv.Engine.HostedServices
{
    /// <summary>
    /// 启动时把 saga 表补齐（缺哪张建哪张，已存在的不动）。
    ///
    /// 放在 HostedService 而不是建表逻辑塞进 <see cref="VivSagaDbContext"/> 构造：建表要 IO，
    /// 而 DbContext 是 Scoped、会被消息处理反复构造 —— 每次 new 都去查一遍库里有什么表太浪费。
    ///
    /// 失败只记日志、不阻断启动：Saga 是可选能力（没配 SagaConnectionString 时这个 HostedService
    /// 根本不会注册），而 saga 库连不上不该让整个 API 起不来。但日志必须留证据 ——
    /// 否则第一条 saga 消息会以「表不存在」的形式落进死信，排查时根本联想不到是这一步。
    /// </summary>
    public sealed class VivSagaTableInitializer : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NanaOptions _option;
        private readonly ILoggerContract _logger;

        public VivSagaTableInitializer(IServiceScopeFactory scopeFactory, NanaOptions option, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _option = option;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_option.SagaConnectionString))
            {
                return;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<VivSagaDbContext>();

                var created = await VivSagaTableCreator
                    .EnsureTablesAsync(context, _option.SagaDatabaseSource, _option.SagaConnectionString, cancellationToken)
                    .ConfigureAwait(false);

                if (created.Count > 0)
                {
                    _logger.Info("Saga 表已创建：{0}", string.Join(", ", created));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Saga 表初始化失败，saga 消息将无法持久化（检查 SagaConnectionString 与建表权限）", ex);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
