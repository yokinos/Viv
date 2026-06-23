using MassTransit;
using Viv.Log;
using Viv.Nana.Core;
using Viv.Nana.Models;

namespace Viv.Nana
{
    /// <summary>
    /// Viv 消费者基类 — 自动处理消息完整性校验和重试逻辑。
    ///
    /// 重试机制：
    /// 1. 子类 <see cref="ReceiveMessageAsync"/> 返回 <see cref="SubscribeResult"/>
    /// 2. 返回 Fail(IsRequeue: true) → 抛出 <see cref="NanaConsumeException"/> → MassTransit 自动重试
    /// 3. 重试策略由 <see cref="NanaMassTransitConfigurationExtensions"/> 中的 UseMessageRetry 控制
    ///    （默认 3 次，间隔 1 秒，全部失败后消息进入死信队列）
    /// 4. 返回 Fail(IsRequeue: false) → 仅记录错误日志，消息直接丢弃不回队
    /// </summary>
    /// <typeparam name="T">消息体类型，必须继承 <see cref="NanaEvent"/></typeparam>
    public abstract class VivConsumer<T> : IConsumer<NanaEnvelope<T>> where T : NanaEvent
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
        /// MassTransit 消费入口（框架内部调用，子类不必关心）
        /// </summary>
        public async Task Consume(ConsumeContext<NanaEnvelope<T>> context)
        {
            if (context == null || context.Message == null || context.Message.Content == null)
                return;

            var result = await ReceiveMessageAsync(context.Message, context.CancellationToken);

            if (result.IsSuccess)
                return;

            if (result.IsRequeue)
            {
                // 抛出异常 → MassTransit 捕获 → 按 RetryCount 自动重试 → 耗尽后死信
                throw new NanaConsumeException(result.Message);
            }

            _logger.Error($"消息消费失败（未回队）: {result.Message}, MessageId: {context.Message.MessageId}");
        }
    }
}
