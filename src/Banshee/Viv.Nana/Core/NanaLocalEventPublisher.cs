using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Wolverine;

namespace Viv.Nana.Core
{
    /// <summary>
    /// 本地事件发布器 —— 把 <see cref="NanaLocalEvent"/> 包成 <see cref="NanaLocalEnvelope{T}"/>
    /// 投进 Wolverine 本地队列。
    ///
    /// 与 <see cref="NanaEventPublisher"/> 平行且互不引用：那个走 RabbitMQ、要包 <c>VivConnectionException</c>；
    /// 本类纯内存，没有网络传输，也就没有可包的连接异常 —— 异常原样冒泡。
    /// </summary>
    public class NanaLocalEventPublisher : IVivLocalEventPublisher
    {
        private readonly IVivContext _context;
        private readonly IMessageBus _bus;
        private readonly ILoggerContract _logger;

        /// <summary>进程内只打一次启动日志（本类是 Scoped，每个作用域都会构造一个实例）</summary>
        private static int _scanLogged;

        public NanaLocalEventPublisher(IVivContext context, IMessageBus bus, ILoggerContract logger)
        {
            _context = context;
            _bus = bus;
            _logger = logger;

            // 排队的拓扑在 AddVivWolverine 注册期就定好了，但那会儿 VivLocator 还没 Initialize、
            // 拿不到 ILoggerContract，所以扫描结论存在 NanaRegister 上，这里首次构造时补一条启动日志。
            if (Interlocked.Exchange(ref _scanLogged, 1) == 0)
            {
                _logger.Info("本地队列已就绪：本地事件 {0} 种", NanaRegister.LocalEventTypeCount);

                // 无消费者的本地事件：消息进队列后无人处理（没有 RabbitMQ 那种无绑定队列即丢弃的兜底）。
                // 一次性全列出来，绝不静默吞 —— 否则表现是「发布成功但什么都没发生」，极难排查。
                foreach (var orphan in NanaRegister.OrphanLocalEvents)
                {
                    _logger.Warning("本地事件无消费者，消息进队列后无人处理：{0}", orphan);
                }
            }
        }

        public async ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent
        {
            if (content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaLocalEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
            };

            await _bus.PublishAsync(message);
            return true;
        }

        public async ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent
        {
            if (content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaLocalEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
            };

            // ⚠️ 纯内存调度：无消息存储时未到期的消息在进程重启后丢失（与 NanaEventPublisher.PublishDelayAsync 同一条路径）
            await _bus.ScheduleAsync(message, delayTTL);
            return true;
        }

        /// <summary>
        /// 快照 + 盖章 holderId。与 <see cref="NanaEventPublisher"/> 里的同名逻辑一致：
        /// 快照自带 HolderId 就不覆盖（重投场景要保留原始 holder），否则取当前进程的 LockHolderContext。
        /// </summary>
        private static VivContextContent SnapshotWithHolder(VivContextContent? snap)
        {
            snap ??= new VivContextContent();
            if (string.IsNullOrWhiteSpace(snap.HolderId))
            {
                snap.HolderId = LockHolderContext.CurrentHolderId;
            }

            return snap;
        }
    }
}
