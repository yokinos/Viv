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
    /// - ❌抢锁失败：二次裁决锁是否真被持有——确实被持有（真竞争）→ ACK确认丢弃，不回队；
    ///   锁未被持有或无法确认（瞬时不稳/Redis故障）→ 抛异常交由 Wolverine 重试，耗尽进死信。
    /// - 未注入 <see cref="IDistributedLock"/>（无Redis环境）：完全跳过锁逻辑。
    ///
    /// 【两套重试机制区分】
    /// ① Wolverine 内存退避重试（业务异常重试）
    /// 子类 <see cref="ReceiveMessageAsync"/> 返回 Fail(IsRequeue:true) → 抛出 <see cref="VivRequeueException"/>
    /// 由 AddVivWolverine 配置 RetryWithCooldown 控制：默认 NanaOptions.RetryCount 次，5s起指数退避，上限600s；耗尽后消息转入死信队列。
    /// 返回 Fail(IsRequeue:false)：仅记录错误日志，消息ACK直接丢弃，不重试。
    ///
    /// ② RabbitMQ延迟重投（业务主动延迟，推荐）
    /// 调用 <see cref="RedeliverAsync"/>，生成全新消息副本，经延迟交换机延时投递Fanout；ReDeliverCount自增，携带DelaySecond；
    /// 原消息直接ACK完成；超过 NanaOptions.RetryCount 重投上限则丢弃副本。
    /// Fanout下全部订阅服务接收副本，依靠消费锁保证单个服务只处理一次。
    ///
    /// 注意：锁服务异常（DistributedLockException）会重新抛出给 Wolverine，由全局重试 + 死信策略兜底；
    /// 锁竞争（IsLockHeldAsync 确认被持有）直接 ACK 丢弃，不会触发重试。
    /// </summary>

    public abstract class VivConsumer<T> where T : NanaEvent
    {
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
                    acquired = await _distributedLock.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(5), holderId).ConfigureAwait(false);
                    if (!acquired)
                    {
                        // 二次裁决：锁确实被其他实例持有 → 真竞争，丢弃不回队；
                        // 锁未被持有但取锁失败（瞬时不稳/命令异常）→ 抛异常交由 Wolverine 重试；
                        // 裁决本身失败（Redis 不可用）→ 异常冒泡，同样交由 Wolverine 重试/进死信。
                        if (await _distributedLock.IsLockHeldAsync(lockKey).ConfigureAwait(false))
                            return;
                        throw new DistributedLockException(lockKey, 0);
                    }
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
