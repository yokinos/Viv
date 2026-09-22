using System.Diagnostics;
using Viv.Contracts;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Log;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana
{
    /// <summary>
    /// Viv 消费者基类 — 封装消息上下文、Redis消费锁、异常与重试编排。
    /// 依赖由 <see cref="VivConsumerDependency"/> 聚合注入（Scoped），子类构造透传即可。
    ///
    /// 上下文：基类 HandleAsync 自动 SetSnapshot / Clear，消息处理完强制清理。
    /// 子类可以手动 SetSnapshot/Clear，但仅限当前消息的 async 执行流内。
    /// 禁止把 _context 捕获进 Task.Run 或后台即忘任务 —— ExecutionContext 会流动，造成多消息上下文串扰。
    /// LockHolderContext 由基类 finally 统一清理，子类不要手动操作。
    ///
    /// 消费锁：HandleAsync 在执行业务 ReceiveMessageAsync 之前抢占 Redis 分布式锁，锁Key
    /// <c>nana:{ServiceName}:{EventType}:{MessageId}</c>。ServiceName 是队列后缀，取自入口程序集；
    /// fanout 下各订阅服务持有各自的锁，互不干扰。抢到 → 执行业务，finally 释放；
    /// 抢不到 → 二次裁决锁是否真被持有：确实被持有（真竞争）ACK 丢弃不回队；
    /// 锁未被持有或无法确认（瞬时不稳 / Redis 故障）→ 抛异常交给 Wolverine 重试，耗尽进死信。
    /// 未注入 <see cref="IDistributedLock"/>（无 Redis 环境）时整段锁逻辑跳过。
    /// 锁 Key 由 <see cref="GetConsumeLockKey"/> 给出，默认就是上面那把消息级去重锁；
    /// 重写它可换成业务粒度（让同一订单的不同消息互斥），返回 null 则这条消息完全不加锁。
    /// 过期时间由 <see cref="GetConsumeLockTime"/> 给出，默认 5 分钟。
    ///
    /// 两套重试机制，别混：
    /// ① Wolverine 内存退避重试（业务异常重试）—— 子类 <see cref="ReceiveMessageAsync"/> 返回
    ///    Fail(IsRequeue:true) 抛 <see cref="VivRequeueException"/>，由 AddVivWolverine 配的 RetryWithCooldown 控制，
    ///    默认 NanaOptions.RetryCount 次，5s 起指数退避、上限 60s，耗尽转死信。
    ///    返回 Fail(IsRequeue:false) 只记错误日志，ACK 直接丢弃，不重试。
    /// ② RabbitMQ 延迟重投（业务主动延迟）—— 调 <see cref="RedeliverAsync"/>，投的是原信封本身，
    ///    经延迟交换机在 delay 后送回 fanout 交换机；不新建信封 —— 新建会丢 MessageId / ReDeliverCount / Context，
    ///    消费端那把去重锁的 Key 就没了。ReDeliverCount 自增、携带 DelaySecond，原消息直接 ACK，
    ///    重投出去的那份才是重试；超过 NanaOptions.RetryCount 上限则丢弃。
    ///    fanout 下所有订阅服务各收一份，靠消费锁保证同服务只处理一次。
    ///
    /// 锁服务异常（DistributedLockException）重新抛给 Wolverine，由全局重试 + 死信兜底；
    /// 锁竞争（IsLockHeldAsync 确认被持有）直接 ACK 丢弃，不触发重试。
    ///
    /// 本地事件分发：子类里经 <see cref="IVivLocalEventBus"/> 入队的本地事件由基类 finally 统一分发，
    /// 消费成功才 Flush，其余路径（抢锁失败 / Requeue / 丢弃 / 抛异常）整队 Discard。放在 finally 是因为
    /// HandleAsync 有四个出口，单点插入会漏；用 else 分支而不是一并 Flush，是为了不顶掉在途的重投异常。
    /// 分发排在 _context?.Clear() 之前，handler 才拿得到租户上下文做过滤。
    ///
    /// 工作单元：子类（或 <see cref="ReceiveMessageAsync"/>）标了 <c>[VivUnitOfWork]</c> 时，
    /// HandleAsync 在取锁成功后显式开事务，业务成功才提交、失败/重投/抛异常回滚。
    /// 提交发生在 Flush 之前；失败路径 Discard。未标特性则不碰事务。
    /// 标了却拿不到 <see cref="IVivUnitOfWork"/>（没配数据库）时构造即失败，不会静默裸奔。
    /// </summary>
    public abstract class VivConsumer<T> where T : NanaEvent
    {
        protected readonly ILoggerContract _logger;

        protected readonly IVivContext _context;

        protected readonly IVivEventPublisher _publisher;

        protected readonly IDistributedLock? _distributedLock;

        protected readonly NanaOptions _nanaOptions;

        protected readonly IVivLocalEventBus _localEventBus;

        private readonly IVivUnitOfWork? _unitOfWork;

        private readonly IVivInbox? _inbox;

        /// <summary>消费锁默认过期时间 —— 重写 <see cref="GetConsumeLockTime"/> 可改</summary>
        private static readonly TimeSpan DefaultConsumeLockExpire = TimeSpan.FromMinutes(5);

        protected VivConsumer(VivConsumerDependency dependency)
        {
            _logger = dependency._logger;
            _context = dependency._context;
            _publisher = dependency._publisher;
            _distributedLock = dependency._distributedLock;
            _nanaOptions = dependency._nanaOptions;
            _localEventBus = dependency._localEventBus;
            _unitOfWork = dependency._unitOfWork;
            _inbox = dependency._inbox;
            ConsumerUnitOfWork.EnsureAvailable(GetType(), _unitOfWork);
        }

        /// <summary>
        /// 业务消费逻辑 — 子类只需实现这个方法，框架处理消费锁、重试、异常、日志
        /// </summary>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default);

        /// <summary>
        /// 可选 Inbox：与业务写同事务记下 <c>(ServiceName, msg:{MessageId})</c>。
        /// 未注入 <see cref="IVivInbox"/> 时恒为 true（不强迫所有消费者启用）。
        /// 返回 false 表示这条已经处理过，调用方应跳过业务。
        /// 这是消息级去重 —— 挡的是同一条消息被投递两次。要按业务粒度去重（同一张订单的不同消息
        /// 只处理一次）用下面那个收业务键的重载。
        /// </summary>
        protected Task<bool> TryAcceptInboxAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default)
        {
            if (_inbox == null) return Task.FromResult(true);
            return _inbox.TryAcceptAsync(envelope.MessageId, cancellationToken);
        }

        /// <summary>
        /// 可选 Inbox 的业务幂等版：键由调用方给，同一件事第二次消费返回 false。
        /// 未注入 <see cref="IVivInbox"/> 时同样恒为 true。
        /// 多租户下记得把租户拼进键（如 <c>$"{_context.SubjectId}:{orderId}"</c>），框架不替你带。
        /// </summary>
        protected Task<bool> TryAcceptInboxAsync(string idempotentKey, CancellationToken cancellationToken = default)
        {
            if (_inbox == null) return Task.FromResult(true);
            return _inbox.TryAcceptAsync(idempotentKey, cancellationToken);
        }

        /// <summary>
        /// Wolverine 消费入口（框架内部调用，子类不必关心）。
        /// 方法名符合 Wolverine handler 约定（HandleAsync + 消息参数），
        /// 由 AddVivWolverine 通过 Discovery.IncludeType 显式注册。
        /// </summary>
        public async Task HandleAsync(NanaEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (envelope == null || envelope.Content == null)
                return;

            object? lockKey = null;
            var acquired = false;
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

                // 取 Key 排在上下文水合之后：重写方要用 _context / LockHolderContext 拼业务 Key，得先看得到本条消息
                lockKey = GetConsumeLockKey(envelope);
                if (_distributedLock != null && lockKey != null)
                {
                    // 非正数在这儿挡掉：真实实现对 expire <= 0 是直接 return false（不抛、不记），
                    // 表现成「取锁失败」→ IsLockHeldAsync 也是 false → 抛 DistributedLockException →
                    // 每条消息都进死信，而错误信息里一个字都不会提过期时间配错了。
                    var lockExpire = GetConsumeLockTime(envelope);
                    if (lockExpire <= TimeSpan.Zero)
                    {
                        _logger.Warning($"消费锁过期时间无效（{lockExpire}），回落默认 {DefaultConsumeLockExpire}: MessageId: {envelope.MessageId}");
                        lockExpire = DefaultConsumeLockExpire;
                    }

                    // 取锁失败 → 二次裁决锁状态，确认是否真竞争 这里的锁采用不可重入模式，避免同一消息在多个服务内消费时重复取锁
                    acquired = await _distributedLock.AcquireLockAsync(lockKey, lockExpire, holderId, false).ConfigureAwait(false);
                    if (!acquired)
                    {
                        // 二次裁决：锁确实被其他实例持有 → 真竞争，丢弃不回队；
                        // 锁未被持有但取锁失败（瞬时不稳/命令异常）→ 抛异常交由 Wolverine 重试；
                        // 裁决本身失败（Redis 不可用）→ 异常冒泡，同样交由 Wolverine 重试/进死信。
                        if (await _distributedLock.IsLockHeldAsync(lockKey).ConfigureAwait(false))
                            return;
                        // 异常只吃 string：Key 允许是任意对象（归一化由 DistributedLockAccessor 内部做），这里只要个可读的标识
                        throw new DistributedLockException(lockKey.ToString()!, 0);
                    }
                }

                var started = Stopwatch.GetTimestamp();
                var result = await ConsumerUnitOfWork.ExecuteAsync(
                    _unitOfWork,
                    GetType(),
                    ct => ReceiveMessageAsync(envelope, ct),
                    cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    NanaMetrics.RecordConsume(typeof(T).Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    succeeded = true;
                    return;
                }

                if (result.IsRequeue)
                {
                    // 抛出异常 → Wolverine 捕获 → 按 RetryCount 自动重试 → 耗尽后进死信
                    throw new VivRequeueException(result.Message);
                }

                _logger.Error($"消息消费失败（未回队）: {result.Message}, MessageId: {envelope.MessageId}");
            }
            catch (DistributedLockException ex)
            {
                // 锁服务故障 / 无法确认锁状态 → 交由 Wolverine 全局策略重试 → 耗尽进死信队列；
                // 真竞争已在取锁处二次裁决为丢弃，不会走到这里。
                _logger.Warning($"分布式锁服务异常，交由 Wolverine 重试/死信: {ex.Message}, MessageId: {envelope.MessageId}");
                throw;
            }
            finally
            {
                if (acquired && _distributedLock != null)
                {
                    try
                    {
                        await _distributedLock.ReleaseLockAsync(lockKey!).ConfigureAwait(false);
                    }
                    catch (Exception relEx)
                    {
                        _logger.Error($"释放消费锁失败 Key: {lockKey}", relEx);
                    }
                }

                // 本地事件分发：消费成功才发，其余路径（重投 / 丢弃 / 异常）整队丢弃 —— 与 HTTP 侧语义一致。
                // 必须排在 _context?.Clear() 之前：handler 与发布方同作用域，要靠 IVivContext 做租户过滤
                // （HTTP 侧 LocalEventFlushMiddleware 必须挂在 VivContextMiddleware 之内是同一个坑）。
                // 传 CancellationToken.None：handler 是主业务的一部分，不因停机而跳过。
                //
                // 失败走 Discard 而非 Flush：Discard 不抛异常，而 FlushAsync 会。
                // finally 里抛出的异常会顶掉在途的 VivRequeueException，把重投语义换成一个不相干的异常 ——
                // 让 flush 只发生在「没有异常在途」的那条路径上，这个坑就不存在。
                if (succeeded)
                    await _localEventBus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                else
                    _localEventBus.Discard();

                _context?.Clear();
                LockHolderContext.Clear();
            }
        }

        /// <summary>
        /// 延迟重投当前消息：ReDeliverCount+1 并携带 DelaySecond，经 RabbitMQ 延迟交换机在 delay 后
        /// 重新投递到 fanout 交换机（各订阅服务各收一份，谁爱消费谁消费；同服务只执行一次由消费锁保证）。
        /// 返回 Success 时原消息正常确认（ack），重投的新副本才是重试——业务直接返回本方法结果即可。
        /// 超过重投上限（NanaOptions.RetryCount）
        /// 返回 Failed(IsRequeue:false)，消息丢弃不回队。
        /// 传输失败抛连接异常（原消息未 ack，Wolverine 重试）；入参无效才返回 Failed 丢弃。
        /// </summary>
        protected async Task<SubscribeResult> RedeliverAsync(NanaEnvelope<T> envelope, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var maxReDeliverCount = _nanaOptions.RetryCount;

            if (envelope.ReDeliverCount >= maxReDeliverCount)
            {
                _logger.Warning($"消息重投已达上限（{maxReDeliverCount} 次），丢弃: MessageId: {envelope.MessageId}");
                return SubscribeResult.Failed(false, $"重投已达上限（{maxReDeliverCount} 次），丢弃");
            }

            envelope.ReDeliverCount++;
            envelope.DelaySecond = delay.TotalSeconds;

            // 入参无效才返回 false；传输失败抛 VivConnectionException，原消息未 ack，由 Wolverine 重试。
            var ok = await _publisher.PublishDelayAsync(delay, envelope, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                _logger.Error($"延迟重投入参无效，消息丢弃: MessageId: {envelope.MessageId}");
                return SubscribeResult.Failed(false, "延迟重投入参无效，丢弃");
            }

            _logger.Info($"消息延迟重投，第 {envelope.ReDeliverCount} 次，{delay.TotalSeconds:0.#} 秒后重投: MessageId: {envelope.MessageId}");
            return SubscribeResult.Success();
        }

        /// <summary>
        /// 本次消费要抢的分布式锁 Key，返回 null 表示这条消息完全不加锁。
        /// 默认是消息级去重锁 <c>nana:{ServiceName}:{EventType}:{MessageId}</c>；
        /// 重写它可以把锁换成业务粒度 —— 比如按订单号锁，让同一订单的不同消息互斥。
        ///
        /// 在上下文水合之后调用，重写方能读到本条消息的 <c>_context</c> 与 <c>LockHolderContext</c>。
        /// 别返回空字符串：那是一个合法的 Key，而 Redis 不接受空 Key，会抛异常把消息送进死信。
        /// </summary>
        protected virtual object? GetConsumeLockKey(NanaEnvelope<T> envelope)
            => NanaRegister.GetConsumerLockKey(typeof(T).Name, envelope.MessageId);

        /// <summary>
        /// 本次消费锁的过期时间，默认 5 分钟。返回非正数会被挡回默认值并记 Warning ——
        /// 真实实现对非正数是不抛异常的取锁失败，放任下去表现成「每条消息都进死信」而看不出原因。
        ///
        /// 它管的是崩溃兜底，不是业务耗时上限：锁取到就自动续期（Redis 实现会在过期时间的一半时续一次），
        /// 业务跑多久锁都不会掉，进程活着就一直续。真出事的场景是进程被 kill —— 续期跟着停，
        /// 锁最多再占这么久。所以调大 = 崩溃后同一把锁占得更久，期间重投回来的消息撞上「真竞争」被丢弃；
        /// 调小 = 恢复快，但留给业务跑完的余量也小。
        ///
        /// 与 <see cref="GetConsumeLockKey"/> 一样在上下文水合之后调用。
        /// </summary>
        protected virtual TimeSpan GetConsumeLockTime(NanaEnvelope<T> envelope) => DefaultConsumeLockExpire;
    }
}
