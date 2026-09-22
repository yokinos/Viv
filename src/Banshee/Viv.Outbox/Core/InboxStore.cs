using System.Reflection;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// <see cref="IVivInbox"/>：可选的消费端幂等。Scoped，与 <c>IMomoDbContext</c> 同作用域才能并入业务事务。
    /// ServiceName 取入口程序集名，与 <c>VivConsumer</c> 消费锁那一段对齐。
    ///
    /// 两族键共用一张表、一个 ServiceName scope，靠前缀分命名空间：消息级去重是 <c>msg:</c>，
    /// 业务级是 <c>biz:</c>。前缀由这里加，调用方只给裸键 —— 不加前缀的话，
    /// 一个数值型业务键（比如订单号 42）会与 MessageId 42 撞成同一行，
    /// 表现是「另一件不相干的事被当成重复跳过」，而表里什么都看不出来。
    /// </summary>
    internal sealed class InboxStore : IVivInbox
    {
        /// <summary>消息级去重键前缀 —— MessageId 由框架产出，不会带冒号</summary>
        private const string MessageKeyPrefix = "msg:";

        /// <summary>业务幂等键前缀 —— 内容是调用方给的裸键，框架不加工</summary>
        private const string BusinessKeyPrefix = "biz:";

        private readonly IInboxRepository _repository;
        private readonly ILoggerContract _logger;
        private readonly string _serviceName;

        public InboxStore(IInboxRepository repository, ILoggerContract logger)
        {
            _repository = repository;
            _logger = logger;
            _serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
        }

        public Task<bool> TryAcceptAsync(long messageId, CancellationToken cancellationToken = default)
        {
            // 0 是空信封的标志，挡在落库之前 —— 放进去就成了「所有 MessageId=0 的消息互相去重」
            if (messageId == 0) return Task.FromResult(false);
            return AcceptAsync($"{MessageKeyPrefix}{messageId}", cancellationToken);
        }

        public Task<bool> TryAcceptAsync(string idempotentKey, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idempotentKey)) return Task.FromResult(false);
            return AcceptAsync($"{BusinessKeyPrefix}{idempotentKey}", cancellationToken);
        }

        private async Task<bool> AcceptAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accepted = await _repository
                .TryInsertAsync(_serviceName, key, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            if (!accepted)
            {
                _logger.Info($"Inbox 已处理过，跳过：ServiceName={_serviceName}, Key={key}");
            }

            return accepted;
        }
    }
}
