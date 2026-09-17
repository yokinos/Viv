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
    ///
    /// 【与 <see cref="VivConsumer{T}"/> 的关系】两者平行、互不继承。差异只有两处，都在消费锁上：
    /// 1. <b>不取 Redis 分布式锁</b>：那条锁是为 fanout 广播下「各订阅服务各收一份、同服务只进一次业务」设计的；
    ///    本地队列就在本进程，点对点一条队列一个消费者，根本不存在多实例竞争。
    /// 2. <b>不 catch DistributedLockException</b>：没有锁可失败。
    /// 另外本类不引入 <see cref="IDistributedLock"/>，依赖由 <see cref="VivLocalConsumerDependency"/> 提供。
    ///
    /// 【上下文约束】同 VivConsumer：基类 HandleAsync 自动 SetSnapshot / 清空，处理完强制清理上下文；
    /// ❗禁止将 _context 捕获/传入 Task.Run、后台即忘任务 —— ExecutionContext 会流动，造成多消息上下文串扰。
    /// 这条对本地队列尤其要紧：消费者本来就跑在后台线程上，再往外 fork 线程，租户上下文就守不住了。
    ///
    /// 【重试】子类 <see cref="ReceiveMessageAsync"/> 返回 Fail(IsRequeue:true) → 抛 <see cref="VivRequeueException"/>
    /// → 由 AddVivWolverine 的全局策略按 NanaOptions.RetryCount 指数退避重试（5s 起、上限 60s）→ 耗尽转入死信队列。
    /// 返回 Fail(IsRequeue:false)：只记错误日志，消息 ACK 丢弃，不重试。
    /// 全局失败策略对本地队列同样生效（不区分端点类型）。
    ///
    /// 【点对点】一个本地事件只有一个消费者（Wolverine 对同一消息类型只认一条 handler chain）。
    /// 要「一个事件触发多个反应」用本地总线（<c>EngineEvent</c> + <c>LocalEventHandler&lt;T&gt;</c>）。
    ///
    /// <b>本版没有 RedeliverAsync</b>（延迟重投）—— 需要延迟再试请先用 Failed(true, ...) 交给退避重试。
    /// </summary>
    /// <typeparam name="T">本地事件类型</typeparam>
    public abstract class VivLocalConsumer<T> where T : NanaLocalEvent
    {
        protected readonly ILoggerContract _logger;

        protected readonly IVivContext _context;

        protected readonly IVivLocalEventPublisher _publisher;

        protected VivLocalConsumer(VivLocalConsumerDependency dependency)
        {
            _logger = dependency._logger;
            _context = dependency._context;
            _publisher = dependency._publisher;
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
        /// 本方法跑在**后台线程 + 独立 DI 作用域**上：注入的 IMomoDbContext / IVivContext 与发布方不是同一份，
        /// 租户上下文靠信封里的快照水合（见 <see cref="NanaLocalEnvelope{T}.Context"/> 的注释）。
        /// </summary>
        public async Task HandleAsync(NanaLocalEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (envelope == null || envelope.Content == null)
                return;

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
                    return;

                if (result.IsRequeue)
                {
                    // 抛出异常 → Wolverine 捕获 → 按 RetryCount 自动重试 → 耗尽后进死信
                    throw new VivRequeueException(result.Message);
                }

                _logger.Error($"本地消息消费失败（未回队）: {result.Message}, MessageId: {envelope.MessageId}");
            }
            finally
            {
                // 正常返回 / 抛出异常 / 重投三条路径统一清理，避免消息上下文残留在后台线程上串到下一条消息
                _context?.Clear();
                LockHolderContext.Clear();
            }
        }
    }
}
