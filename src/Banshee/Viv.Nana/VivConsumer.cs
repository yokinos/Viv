using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// Viv 消费者基类 — 自动处理消息完整性校验和重试逻辑。
    ///
    /// 重试机制（基于 Wolverine 全局失败策略，见 VivWolverineConfigurationExtensions）：
    /// 1. 子类 <see cref="ReceiveMessageAsync"/> 返回 <see cref="SubscribeResult"/>
    /// 2. 返回 Fail(IsRequeue: true) → 抛出 <see cref="VivRequeueException"/> → Wolverine 自动重试
    /// 3. 重试策略由 AddVivWolverine 中的 RetryWithCooldown 控制
    ///    （默认 NanaOptions.RetryCount 次，间隔 1 秒，全部失败后消息进入死信队列）
    /// 4. 返回 Fail(IsRequeue: false) → 仅记录错误日志，消息直接丢弃不回队
    /// </summary>
    /// <typeparam name="T">消息体类型，必须继承 <see cref="NanaEvent"/></typeparam>
    public abstract class VivConsumer<T> where T : NanaEvent
    {
        protected readonly ILoggerContract _logger;

        protected VivConsumer(ILoggerContract logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 业务消费逻辑 — 子类只需实现这个方法，框架处理重试、异常、日志
        /// </summary>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<T> message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Wolverine 消费入口（框架内部调用，子类不必关心）。
        /// 方法名符合 Wolverine handler 约定（HandleAsync + 消息参数），
        /// 由 AddVivWolverine 通过 Discovery.IncludeType 显式注册。
        /// </summary>
        public async Task HandleAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (envelope == null || envelope.Content == null)
                return;

            var result = await ReceiveMessageAsync(envelope, cancellationToken);

            if (result.IsSuccess)
                return;

            if (result.IsRequeue)
            {
                // 抛出异常 → Wolverine 捕获 → 按 RetryCount 自动重试 → 耗尽后进死信
                throw new VivRequeueException(result.Message);
            }

            _logger.Error($"消息消费失败（未回队）: {result.Message}, MessageId: {envelope.MessageId}");
        }
    }
}
