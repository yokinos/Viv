using System.Diagnostics;
using System.Text.Json;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Momo;
using Viv.Nana;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// <see cref="IVivOutbox"/> 的实现：把事件写进发件箱，不发任何东西。
    ///
    /// 必须是 Scoped —— 与业务代码拿到的 <c>IMomoDbContext</c> 同一作用域才能共享事务状态
    /// （Momo 的 <c>_transaction</c> 挂在实例字段上）。换成 Singleton 或 Transient，
    /// 入队就跑到业务事务外面去了，原子性当场归零 —— 所以 <see cref="EnqueueAsync{T}"/> 第一件事
    /// 就是确认自己确实在那个事务里，不在就直接抛。
    /// </summary>
    internal sealed class OutboxStore : IVivOutbox
    {
        private readonly IOutboxRepository _repository;
        private readonly IMomoDbContext _db;
        private readonly IVivContext _context;
        private readonly ILoggerContract _logger;
        private readonly TimeProvider _time;

        public OutboxStore(
            IOutboxRepository repository,
            IMomoDbContext db,
            IVivContext context,
            ILoggerContract logger,
            TimeProvider? timeProvider = null)
        {
            _repository = repository;
            _db = db;
            _context = context;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
        }

        public async Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            // 发件箱的唯一价值是「业务写与入队同时成立」，而这个保证完全由调用方开的事务提供 ——
            // 方法自己开不了：它连业务写都不知道在哪。所以这里只验证、不代劳。
            //
            // 之所以要抛而不是记日志：不抛的话，调用方从返回值看不出区别（照样 true），
            // 结果是「消息已落库、事件永不投递」，等发现时只能靠对账。而这类疏漏的成因往往是
            // 「以为外层包了事务」，靠 code review 拦不住，只能靠运行时炸出来。
            //
            // 需要合法地不参与事务（补偿脚本、离线补数据）时，别绕过这个断言 ——
            // 那种场景应该用「不依赖业务写」的路径投递，而不是往发件箱里塞一条永远对不上账的记录。
            if (!_db.IsInTransaction)
            {
                throw new InvalidOperationException(
                    "发件箱入队必须在事务内进行，当前 IMomoDbContext 不在事务中：" +
                    "业务写与入队要么一起提交、要么一起回滚，否则会出现「消息落了、事件永不投递」。\n" +
                    "  · API/服务层：用 IMomoDbContext.ExecuteInTransactionAsync 包住「业务写 + 入队」" +
                    "（参考 AgentChatService.AcceptTurnAsync）或用 [VivUnitOfWork]（参考 ChatService）\n" +
                    "  · 消费者：短事务只包写入段，模型调用等长耗时动作留在事务外，不要把整个 HandleAsync 塞进事务\n" +
                    "  · 若确实不该原子：不要走发件箱，改用 IVivEventPublisher / IVivLocalEventPublisher");
            }

            content.DeliverySource = DeliverySource.Outbox;
            var envelope = new NanaEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
            };

            // 统一取一次当前时刻给两个时间列：投递器判断「到期没到期」用的也是 C# 侧的时间
            // （不依赖 SYSUTCDATETIME() / now()，省得两端时钟口径不一致）。
            // 走注入的时钟是为了让「入队时刻」在测试里可断言，也为了将来换授时源只改一处。
            var now = _time.GetUtcNow().UtcDateTime;

            var message = new OutboxMessage
            {
                Id = IdMagic.NextId(),
                MessageId = envelope.MessageId,
                EventType = typeof(T).FullName ?? typeof(T).Name, // FullName 可能为 null（泛型类型），用 Name 兜底
                Payload = JsonSerializer.Serialize(envelope, OutboxJson.Options),
                Status = OutboxStatus.Pending,
                RetryCount = 0,
                NextRetryAt = now,
                OccurredAt = now,
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
