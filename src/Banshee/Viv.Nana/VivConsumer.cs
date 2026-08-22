using Viv.Aoi;
using Viv.Contracts;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// Viv 消费者基类 — 自动处理消息完整性校验和重试逻辑。
    ///
    /// 重要约束：
    /// 1. 基类HandleAsync负责 SetSnapshot / Clear，消息结束会自动清理上下文；
    /// 2. 子类可使用 _context 读取，**允许手动SetSnapshot/Clear，但必须保证在当前消息的async流内部，不能泄露到Task.Run后台任务**；
    /// 3. 禁止将 _context 实例传递、闭包捕获到 Task.Run / 后台火后即忘任务；后台任务会Flow ExecutionContext，会继承当前消息快照，造成上下文串扰；
    /// 4. LockHolderContext 由基类finally统一清理，子类不要手动管理。
    ///
    /// 重试机制（基于 Wolverine 全局失败策略，见 VivWolverineConfigurationExtensions）：
    /// 1. 子类 <see cref="ReceiveMessageAsync"/> 返回 <see cref="SubscribeResult"/>
    /// 2. 返回 Fail(IsRequeue: true) → 抛出 <see cref="VivRequeueException"/> → Wolverine 自动重试
    /// 3. 重试策略由 AddVivWolverine 中的 RetryWithCooldown 控制
    ///    （默认 NanaOptions.RetryCount 次，间隔 1 秒，全部失败后消息进入死信队列）
    /// 4. 返回 Fail(IsRequeue: false) → 仅记录错误日志，消息直接丢弃不回队
    /// </summary>
    public abstract class VivConsumer<T> where T : NanaEvent
    {
        protected readonly ILoggerContract _logger;

        protected readonly IVivContext _context;

        protected VivConsumer(ILoggerContract logger, IVivContext context)
        {
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// 业务消费逻辑 — 子类只需实现这个方法，框架处理重试、异常、日志
        /// </summary>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default);

        /// <summary>
        /// Wolverine 消费入口（框架内部调用，子类不必关心）。
        /// 方法名符合 Wolverine handler 约定（HandleAsync + 消息参数），
        /// 由 AddVivWolverine 通过 Discovery.IncludeType 显式注册。
        /// </summary>
        public async Task HandleAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (envelope == null || envelope.Content == null)
                return;

            try
            {
                if (envelope.Context != null)
                {
                    // 将上下文注入
                    _context.SetSnapshot(envelope.Context);
                    // 设置分布式锁的持有者Id
                    LockHolderContext.SetHolderId(_context.TraceId);
                }

                var result = await ReceiveMessageAsync(envelope, cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess)
                    return;

                if (result.IsRequeue)
                {
                    // 抛出异常 → Wolverine 捕获 → 按 RetryCount 自动重试 → 耗尽后进死信
                    throw new VivRequeueException(result.Message);
                }

                _logger.Error($"消息消费失败（未回队）: {result.Message}, MessageId: {envelope.MessageId}");
            }
            catch (DistributedLockException ex)
            {
                // 拿不到锁 = 已有其他实例/服务在消费同一消息（锁 Key 通常为 MessageId），
                // 按「拿到执行、拿不到丢弃」契约丢弃不回队，避免空转重试后进死信。
                _logger.Warning($"获取分布式锁失败，消息丢弃（不回队）: {ex.Message}, MessageId: {envelope.MessageId}");
                return;
            }
            finally
            {
                _context?.Clear();
                LockHolderContext.Clear();
            }
        }
    }
}
