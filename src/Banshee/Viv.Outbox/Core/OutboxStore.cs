using System.Diagnostics;
using System.Text.Json;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Nana;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// <see cref="IVivOutbox"/> 的实现：把事件写进发件箱，不发任何东西。
    ///
    /// 必须是 Scoped —— 与业务代码拿到的 <c>IMomoDbContext</c> 同一作用域才能共享事务状态
    /// （Momo 的 <c>_transaction</c> 挂在实例字段上）。换成 Singleton 或 Transient，
    /// 入队就跑到业务事务外面去了，原子性当场归零。
    /// </summary>
    internal sealed class OutboxStore : IVivOutbox
    {
        private readonly IOutboxRepository _repository;
        private readonly IVivContext _context;
        private readonly ILoggerContract _logger;

        public OutboxStore(IOutboxRepository repository, IVivContext context, ILoggerContract logger)
        {
            _repository = repository;
            _context = context;
            _logger = logger;
        }

        public async Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var envelope = new NanaEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
            };

            // 统一取一次 UtcNow 给两个时间列：投递器判断「到期没到期」用的也是 C# 侧的时间
            // （不依赖 SYSUTCDATETIME() / now()，省得两端时钟口径不一致）。
            var now = DateTime.UtcNow;

            var message = new OutboxMessage
            {
                Id = IdMagic.NextId(),
                MessageId = envelope.MessageId,
                // 存 FullName 而不是程序集限定名：AQN 里带着程序集版本号，
                // 一次发版就会让库里旧行的类型解析不出来。
                EventType = typeof(T).FullName ?? typeof(T).Name,
                Payload = JsonSerializer.Serialize(envelope, OutboxJson.Options),
                Status = OutboxStatus.Pending,
                RetryCount = 0,
                NextRetryAt = now,
                OccurredAt = now,

                // 只有这一刻取得到。投递器跑在后台作用域里，Activity.Current 是 null，
                // 请求上下文也早没了，入队时不存就等于永久丢。
                //
                // 请求 Id 走 IVivContext 而不是 HttpContext：Viv.Outbox 是纯 Microsoft.NET.Sdk，
                // 看不到 IHttpContextAccessor，而 VivContextMiddleware 填的就是 context.TraceIdentifier。
                // 无请求上下文时它给的是空串，存成 NULL —— 空串在库里长得像「有但是空的」。
                TraceId = Activity.Current?.TraceId.ToString(),
                RequestTraceId = string.IsNullOrWhiteSpace(_context.TraceId) ? null : _context.TraceId,
            };

            var inserted = await _repository.InsertAsync(message, cancellationToken).ConfigureAwait(false);

            if (inserted)
            {
                OutboxMetrics.RecordEnqueue();
            }

            // false 只表示影响 0 行。落库没生效必须让人看见 —— 静默返回会让业务
            // 以为消息已经进了发件箱，而它其实永远不会被投递出去。
            if (!inserted)
            {
                _logger.Error(
                    $"发件箱入队未生效（影响 0 行）：EventType={message.EventType}, MessageId={message.MessageId}, Id={message.Id}");
            }

            return inserted;
        }

        /// <summary>
        /// 与 <c>NanaEventPublisher</c> 同一套盖章逻辑（含 holderId 兜底）。只抄不共用 ——
        /// 共用得往 Viv.Nana 里塞一个双方都依赖的内部 helper，为三行代码不值当。
        /// </summary>
        private static VivContextContent SnapshotWithHolder(VivContextContent? snapshot)
        {
            snapshot ??= new VivContextContent();
            if (string.IsNullOrWhiteSpace(snapshot.HolderId))
            {
                snapshot.HolderId = LockHolderContext.CurrentHolderId;
            }

            return snapshot;
        }
    }
}
