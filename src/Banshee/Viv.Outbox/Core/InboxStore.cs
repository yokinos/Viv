using System.Reflection;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// <see cref="IVivInbox"/>：可选的消费端幂等。Scoped，与 <c>IMomoDbContext</c> 同作用域才能并入业务事务。
    /// ServiceName 取入口程序集名，与 <c>VivConsumer</c> 消费锁那一段对齐。
    /// </summary>
    internal sealed class InboxStore : IVivInbox
    {
        private readonly IInboxRepository _repository;
        private readonly ILoggerContract _logger;
        private readonly string _serviceName;

        public InboxStore(IInboxRepository repository, ILoggerContract logger)
        {
            _repository = repository;
            _logger = logger;
            _serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
        }

        public async Task<bool> TryAcceptAsync(long messageId, CancellationToken cancellationToken = default)
        {
            if (messageId == 0) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var accepted = await _repository
                .TryInsertAsync(_serviceName, messageId, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            if (!accepted)
            {
                _logger.Info($"Inbox 重复投递已跳过：ServiceName={_serviceName}, MessageId={messageId}");
            }

            return accepted;
        }
    }
}
