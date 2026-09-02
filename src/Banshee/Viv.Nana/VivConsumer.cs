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
    ///
    /// 【上下文约束】
    /// 1. 基类 HandleAsync 自动执行 SetSnapshot / Clear，消息处理完成强制清理上下文；
    /// 2. 子类允许手动 SetSnapshot/Clear，但仅限**当前消息的async执行流内**；
    /// 3. ❗禁止将 _context 捕获/传入 Task.Run、后台即忘任务；ExecutionContext 会发生流动，造成多消息上下文串扰；
    /// 4. LockHolderContext 由基类 finally 统一释放清理，子类切勿手动操作。
    ///
    /// 【消费锁逻辑】
    /// HandleAsync 在执行业务 ReceiveMessageAsync 之前，抢占 Redis 分布式锁，锁Key：
    /// <c>nana:{ServiceName}:{EventType}:{MessageId}</c>
    /// - ServiceName：队列后缀，取自入口程序集；Fanout广播模式下，每个订阅服务持有独立锁，互不干扰。
    /// - ✅获取锁：执行业务逻辑，finally块自动释放锁；
    /// - ❌抢锁失败：默认ACK确认原消息，直接丢弃，不会回原始队列、不会触发Wolverine重试、不进死信；
    ///   若事件标记 <see cref="NanaEvent.LockFailShouldRetryDeliver"/> = true，且未达重投上限，则走延迟重投；
    ///   重投到达上限依旧执行丢弃。
    /// - 未注入 <see cref="IDistributedLock"/>（无Redis环境）：完全跳过锁逻辑。
    ///
    /// 【两套重试机制区分】
    /// ① Wolverine 内存退避重试（业务异常重试）
    /// 子类 <see cref="ReceiveMessageAsync"/> 返回 Fail(IsRequeue:true) → 抛出 <see cref="VivRequeueException"/>
    /// 由 AddVivWolverine 配置 RetryWithCooldown 控制：默认 NanaOptions.RetryCount 次，5s起指数退避，上限600s；耗尽后消息转入死信队列。
    /// 返回 Fail(IsRequeue:false)：仅记录错误日志，消息ACK直接丢弃，不重试。
    ///
    /// ② RabbitMQ延迟重投（锁竞争/业务主动延迟，推荐）
    /// 调用 <see cref="RedeliverAsync"/>，生成全新消息副本，经延迟交换机延时投递Fanout；ReDeliverCount自增，携带DelaySecond；
    /// 原消息直接ACK完成；超过 NanaOptions.RetryCount 重投上限则丢弃副本。
    /// Fanout下全部订阅服务接收副本，依靠消费锁保证单个服务只处理一次。
    ///
    /// 注意：分布式锁抢占失败的异常会被基类捕获消化，**不会抛给Wolverine，内存重试策略对锁竞争无效**。
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
                        return;// 拿不到锁 = 已有其他实例在消费同一消息，按契约丢弃不回队
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
