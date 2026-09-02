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
    /// Viv 消费者基类 — 自动处理消息完整性校验、消费锁和重试逻辑。
    ///
    /// 重要约束：
    /// 1. 基类HandleAsync负责 SetSnapshot / Clear，消息结束会自动清理上下文；
    /// 2. 子类可使用 _context 读取，**允许手动SetSnapshot/Clear，但必须保证在当前消息的async流内部，不能泄露到Task.Run后台任务**；
    /// 3. 禁止将 _context 实例传递、闭包捕获到 Task.Run / 后台火后即忘任务；后台任务会Flow ExecutionContext，会继承当前消息快照，造成上下文串扰；
    /// 4. LockHolderContext 由基类finally统一清理，子类不要手动管理。
    ///
    /// 消费锁（框架级，谁取到锁谁进业务）：
    /// HandleAsync 在调用 <see cref="ReceiveMessageAsync"/> 前按
    /// <c>nana:{ServiceName}:{EventType}:{MessageId}</c> 取 Redis 分布式锁。
    /// ServiceName 与队列后缀相同（入口程序集名），fanout 下各订阅服务各持一把锁、各处理一份。
    /// 取到 → 进业务，结束释放；拿不到 → 视为已有实例在处理，走既有 DistributedLockException 契约
    /// （默认丢弃 ack；<see cref="NanaEvent.LockFailShouldRetryDeliver"/> 则延迟重投）。
    /// 未注册 <see cref="IDistributedLock"/>（无 Redis）时跳过取锁，行为与加锁前一致。
    ///
    /// 重试机制（Wolverine 全局失败策略 + 延迟重投）：
    /// 1. 子类 <see cref="ReceiveMessageAsync"/> 返回 <see cref="SubscribeResult"/>
    /// 2. 返回 Fail(IsRequeue: true) → 抛出 <see cref="VivRequeueException"/> → Wolverine 内存退避重试
    /// 3. 重试策略由 AddVivWolverine 中的 RetryWithCooldown 控制
    ///    （默认 NanaOptions.RetryCount 次，指数退避 5s 起、最大 600s，全部失败后消息进入死信队列）
    /// 4. 返回 Fail(IsRequeue: false) → 仅记录错误日志，消息直接丢弃不回队
    /// 5. 延迟重投（推荐）→ 调用 <see cref="RedeliverAsync"/>：把原消息经 RabbitMQ 延迟交换机在指定
    ///    延迟后重新投递到 fanout（各订阅服务各收一份），ReDeliverCount+1
    ///    并携带 DelaySecond；超过 NanaOptions.RetryCount 上限则丢弃（不回队）。
    /// </summary>
    public abstract class VivConsumer<T> where T : NanaEvent
    {
        /// <summary>消费锁 TTL：覆盖一次业务处理时长，并靠 Redis 续期；处理完即释放。</summary>
        private static readonly TimeSpan ConsumerLockExpire = TimeSpan.FromMinutes(5);

        protected readonly ILoggerContract _logger;

        protected readonly IVivContext _context;

        protected readonly IVivEventPublisher _publisher;

        private readonly IDistributedLock? _distributedLock;

        protected VivConsumer(VivConsumerDependency dependency)
        {
            _logger = dependency._logger;
            _context = dependency._context;
            _publisher = dependency._publisher;
            _distributedLock = dependency._distributedLock;
        }

        /// <summary>
        /// 业务消费逻辑 — 子类只需实现这个方法，框架处理消费锁、重试、异常、日志
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

            var lockKey = NanaRegister.GetConsumerLockKey(typeof(T).Name, envelope.MessageId);
            var acquired = false;

            try
            {
                var holderId = envelope.MessageId.ToString();
                if (envelope.Context != null)
                {
                    _context.SetSnapshot(envelope.Context);
                    if (!string.IsNullOrEmpty(_context.TraceId))
                        holderId = _context.TraceId;
                }
                LockHolderContext.SetHolderId(holderId);

                if (_distributedLock != null)
                {
                    acquired = await _distributedLock.AcquireLockAsync(lockKey, ConsumerLockExpire, holderId).ConfigureAwait(false);
                    if (!acquired)
                        throw new DistributedLockException(lockKey, 0);
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
                // 仅「纯拿锁失败」（InnerException == null，非业务/Redis 异常）+ 消息声明重投时自动延迟重投；
                // 已安排重投则原消息正常确认（ack），不打印「丢弃」
                if (ex.InnerException == null && envelope.Content.LockFailShouldRetryDeliver)
                {
                    var retry = await RedeliverAsync(envelope, TimeSpan.FromMinutes(2 * (envelope.ReDeliverCount + 1)), cancellationToken);
                    if (retry.IsSuccess)
                        return;
                    // 达重投上限 → RedeliverAsync 已记 warning，落入下方丢弃日志
                }

                // 拿不到锁 = 已有其他实例在消费同一消息（锁 Key = 服务名+事件+MessageId），
                // 按「拿到执行、拿不到丢弃」契约丢弃不回队，避免空转重试后进死信。
                _logger.Warning($"获取分布式锁失败，消息丢弃（不回队）: {ex.Message}, MessageId: {envelope.MessageId}");
                return;
            }
            finally
            {
                if (acquired && _distributedLock != null)
                {
                    try
                    {
                        await _distributedLock.ReleaseLockAsync(lockKey).ConfigureAwait(false);
                    }
                    catch (Exception relEx)
                    {
                        _logger.Error($"释放消费锁失败 Key: {lockKey}", relEx);
                    }
                }

                _context?.Clear();
                LockHolderContext.Clear();
            }
        }

        /// <summary>
        /// 延迟重投当前消息：ReDeliverCount+1 并携带 DelaySecond，经 RabbitMQ 延迟交换机在 delay 后
        /// 重新投递到 fanout 交换机（各订阅服务各收一份，谁爱消费谁消费；同服务只执行一次由消费锁保证）。
        /// 返回 Success 时原消息正常确认（ack），重投的新副本才是重试——业务直接返回本方法结果即可。
        /// 超过重投上限（NanaOptions.RetryCount，经 VivConfigRegistry 静态取，见 NanaRegister.Initialize）
        /// 返回 Failed(IsRequeue:false)，消息丢弃不回队。
        /// </summary>
        protected async Task<SubscribeResult> RedeliverAsync(NanaEnvelope<T> envelope, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var maxReDeliverCount = (VivConfigRegistry.Get<NanaOptions>() ?? new NanaOptions()).RetryCount;

            if (envelope.ReDeliverCount >= maxReDeliverCount)
            {
                _logger.Warning($"消息重投已达上限（{maxReDeliverCount} 次），丢弃: MessageId: {envelope.MessageId}");
                return SubscribeResult.Failed(false, $"重投已达上限（{maxReDeliverCount} 次），丢弃");
            }

            envelope.ReDeliverCount++;
            envelope.DelaySecond = delay.TotalSeconds;

            var ok = await _publisher.PublishDelayAsync(delay, envelope, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                _logger.Error($"延迟重投发布失败，消息丢弃: MessageId: {envelope.MessageId}");
                return SubscribeResult.Failed(false, "延迟重投发布失败，丢弃");
            }

            _logger.Info($"消息延迟重投，第 {envelope.ReDeliverCount} 次，{delay.TotalSeconds:0.#} 秒后重投: MessageId: {envelope.MessageId}");
            return SubscribeResult.Success();
        }
    }
}
