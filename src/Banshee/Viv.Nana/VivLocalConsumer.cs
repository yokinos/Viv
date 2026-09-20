using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// Viv 本地队列消费者基类 — 封装消息上下文、异常与重试编排。
    /// <list type="bullet">
    /// <item><description>与 <see cref="VivConsumer{T}"/> 平行独立，不继承；差异仅在消费锁：不使用Redis分布式锁。Redis锁用于Fanout广播场景，实现多服务订阅、同服务防重复执行；本地队列运行于当前进程，单队列单消费者，不存在多实例竞争，也不捕获 DistributedLockException。依赖由 <see cref="VivLocalConsumerDependency"/> 注入。</description></item>
    /// <item><description>上下文管理：HandleAsync 自动执行 SetSnapshot / 清空，消息处理完毕强制清理。禁止将 _context 捕获传入 Task.Run 或后台即忘任务；ExecutionContext 会发生流动，引发多消息上下文串扰。本地队列消费者本身运行在后台线程，该约束尤为重要，防止租户上下文丢失。</description></item>
    /// <item><description>重试机制：返回 Fail(IsRequeue:true) 抛出 <see cref="VivRequeueException"/>，由 AddVivWolverine 全局策略按 NanaOptions.RetryCount 指数退避重试（起始5s，上限60s），重试耗尽转入死信队列；返回 Fail(IsRequeue:false)，仅记录错误日志，消息ACK直接丢弃。</description></item>
    /// <item><description>点对点约束：一个本地事件仅绑定一条消费者链路，Wolverine针对同一消息类型只识别一套handler chain。如需一个事件触发多个业务响应，请使用本地事件总线。</description></item>
    /// <item><description>本地事件分发：子类通过 <see cref="IVivLocalEventBus"/> 入队的本地事件，由基类 finally 统一分发；消费成功才执行 Flush，其余场景（Requeue / 丢弃 / 抛出异常）全部 Discard。天然保证顺序：消息业务提交在前，事件分发在后，即「提交 → 分发」。</description></item>
    /// <item><description>能力说明：本基类不提供 RedeliverAsync；如需延迟重试，返回 Failed(true, ...) 交由框架退避重试处理。</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">本地事件类型</typeparam>
    public abstract class VivLocalConsumer<T> where T : NanaLocalEvent
    {
        protected readonly ILoggerContract _logger;

        protected readonly IVivContext _context;

        protected readonly IVivLocalEventPublisher _publisher;

        protected readonly IVivLocalEventBus _localEventBus;

        protected VivLocalConsumer(VivLocalConsumerDependency dependency)
        {
            _logger = dependency._logger;
            _context = dependency._context;
            _publisher = dependency._publisher;
            _localEventBus = dependency._localEventBus;
        }

        /// <summary>
        /// 业务消费逻辑 — 子类只需实现这个方法，框架处理上下文水合、重试、异常、日志
        /// </summary>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<T> envelope, CancellationToken cancellationToken = default);

        /// <summary>
        /// Wolverine 消费入口（框架内部调用，子类不必关心）。
        /// 方法名符合 Wolverine handler 约定（HandleAsync + 消息参数），
        /// 由 AddVivWolverine 通过 Discovery.IncludeType 显式注册。
        ///
        /// 本方法跑在后台线程 + 独立 DI 作用域上：注入的 IMomoDbContext / IVivContext 与发布方不是同一份，
        /// 租户上下文靠信封里的快照水合（见 <see cref="NanaLocalEnvelope{T}.Context"/> 的注释）。
        /// </summary>
        public async Task HandleAsync(NanaLocalEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (envelope == null || envelope.Content == null)
                return;

            var succeeded = false;

            try
            {
                var holderId = envelope.MessageId.ToString();
                if (envelope.Context != null)
                {
                    if (!envelope.Context.IsEmpty())
                        _context.SetSnapshot(envelope.Context);

                    if (!string.IsNullOrWhiteSpace(envelope.Context.HolderId))
                        holderId = envelope.Context.HolderId;
                }

                LockHolderContext.SetHolderId(holderId);

                var result = await ReceiveMessageAsync(envelope, cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    succeeded = true;
                    return;
                }

                if (result.IsRequeue)
                {
                    // 抛出异常 → Wolverine 捕获 → 按 RetryCount 自动重试 → 耗尽后进死信
                    throw new VivRequeueException(result.Message);
                }

                _logger.Error($"本地消息消费失败（未回队）: {result.Message}, MessageId: {envelope.MessageId}");
            }
            finally
            {
                // 本地事件分发：消费成功才发，其余路径（重投 / 丢弃 / 异常）整队丢弃 —— 与 HTTP 侧语义一致。
                // 必须排在 _context?.Clear() 之前：handler 与发布方同作用域，要靠 IVivContext 做租户过滤。
                // 传 CancellationToken.None：handler 是主业务的一部分，不因停机而跳过。
                //
                // 失败走 Discard 而非 Flush：Discard 不抛异常，而 FlushAsync 会。
                // finally 里抛出的异常会顶掉在途的 VivRequeueException，把重投语义换成一个不相干的异常。
                if (succeeded)
                    await _localEventBus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                else
                    _localEventBus.Discard();

                // 正常返回 / 抛出异常 / 重投三条路径统一清理，避免消息上下文残留在后台线程上串到下一条消息
                _context?.Clear();
                LockHolderContext.Clear();
            }
        }
    }
}
