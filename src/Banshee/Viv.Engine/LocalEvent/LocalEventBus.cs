using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.LocalEvent
{
    /// <summary>
    /// 本地事件总线实现 —— 注册为 <b>Scoped</b>（与 IMomoDbContext、IVivContext 同作用域，这是整个设计的支点）。
    ///
    /// 生命周期三态：Pending（可入队）→ Draining（分发中，仍可入队）→ Done（终态，丢弃新事件）。
    /// </summary>
    internal sealed class LocalEventBus : IVivLocalEventBus, IDisposable
    {
        /// <summary>单次 Flush 最多分发多少轮 —— 处理器内可再发布，轮数上限防递归死循环</summary>
        private const int MaxDrainRounds = 5;

        private readonly ILoggerContract _logger;
        private readonly Dictionary<Type, ILocalEventHandlerInvoker> _invokers;

        /// <summary>入队/清空/状态迁移的统一锁</summary>
        private readonly object _sync = new();

        /// <summary>待发事件队列，FIFO —— 分发顺序 = 发布顺序</summary>
        private readonly Queue<EngineEvent> _pending = new();

        private LocalEventBusState _state = LocalEventBusState.Pending;

        /// <summary>进程内只打一次启动信息（每个作用域都会构造一个总线实例）</summary>
        private static int _scanLogged;

        public LocalEventBus(IEnumerable<ILocalEventHandlerInvoker> invokers, ILoggerContract logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 一个事件类型只注册一个分发器，ToDictionary 在此处顺带做重复注册的兜底断言
            _invokers = (invokers ?? throw new ArgumentNullException(nameof(invokers)))
                .ToDictionary(x => x.EventType);

            if (Interlocked.Exchange(ref _scanLogged, 1) == 0)
            {
                _logger.Info("本地事件总线已就绪：事件类型 {0} 种，处理器 {1} 个",
                    LocalEventRegistration.EventTypeCount, LocalEventRegistration.HandlerCount);
            }
        }

        /// <inheritdoc />
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : EngineEvent
        {
            ArgumentNullException.ThrowIfNull(@event);

            lock (_sync)
            {
                if (_state == LocalEventBusState.Done)
                {
                    // 分发已结束（或整队已丢弃）后还在发布：作用域用错了，记日志不静默吞
                    _logger.Warning("本地事件在作用域分发结束后发布，已丢弃：{0}", typeof(TEvent).Name);
                    return Task.CompletedTask;
                }

                // Draining 态**允许**入队：处理器内递归发布是合法用法，新事件进下一轮 drain
                _pending.Enqueue(@event);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task FlushAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                // 幂等：已分发过（或已丢弃）直接返回
                if (_state == LocalEventBusState.Done)
                    return;

                _state = LocalEventBusState.Draining;
            }

            try
            {
                for (var round = 0; round < MaxDrainRounds; round++)
                {
                    List<EngineEvent> batch;
                    lock (_sync)
                    {
                        // 快照 + 清空：分发期间新发布的事件进下一轮，不干扰本轮遍历
                        if (_pending.Count == 0)
                            return;

                        batch = new List<EngineEvent>(_pending);
                        _pending.Clear();
                    }

                    foreach (var @event in batch)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!_invokers.TryGetValue(@event.GetType(), out var invoker))
                        {
                            // 事件类型无处理器是合法的（可能只是没人关心），但必须留痕
                            _logger.Warning("本地事件无处理器，已跳过：{0}", @event.GetType().Name);
                            continue;
                        }

                        // 处理器抛异常直接向上冒泡：本地事件是主业务流的一部分，不静默失败
                        await invoker.InvokeAsync(@event, ct).ConfigureAwait(false);
                    }
                }

                bool hasRemainder;
                lock (_sync)
                {
                    hasRemainder = _pending.Count > 0;
                }

                if (hasRemainder)
                {
                    _logger.Error("本地事件疑似处理器递归发布，已达 {0} 轮上限，剩余事件已丢弃", MaxDrainRounds);
                }
            }
            finally
            {
                // 正常结束 / 提前 return / 处理器抛异常，三种路径统一收口为终态并清空残留
                lock (_sync)
                {
                    _pending.Clear();
                    _state = LocalEventBusState.Done;
                }
            }
        }

        /// <inheritdoc />
        public void Discard()
        {
            lock (_sync)
            {
                if (_state == LocalEventBusState.Done)
                    return;

                // 业务失败 → 整队丢弃，一条事件都不发，不留幽灵事件
                _pending.Clear();
                _state = LocalEventBusState.Done;
            }
        }

        /// <summary>
        /// 作用域结束时兜底：说明调用方忘了走 Flush/Discard（或该宿主没有触发点，如 Worker / TickerQ）。
        /// 只记 Warning，绝不静默吞。
        /// </summary>
        public void Dispose()
        {
            int remaining;
            lock (_sync)
            {
                remaining = _pending.Count;
                _pending.Clear();
                _state = LocalEventBusState.Done;
            }

            if (remaining > 0)
            {
                _logger.Warning("本地事件未分发（作用域结束前未触发 Flush/Discard），共 {0} 条已丢弃", remaining);
            }
        }

        /// <summary>总线生命周期状态</summary>
        private enum LocalEventBusState
        {
            /// <summary>可入队，尚未开始分发</summary>
            Pending = 0,

            /// <summary>分发中，仍可入队（处理器内递归发布）</summary>
            Draining = 1,

            /// <summary>终态：已分发完 / 已丢弃，新事件一律丢弃</summary>
            Done = 2,
        }
    }
}
