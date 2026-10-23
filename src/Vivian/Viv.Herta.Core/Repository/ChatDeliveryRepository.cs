using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Herta.Core.Repository
{
    /// <summary>
    /// 投递水位。一个用户一行，Ack 只推水位不记明细（明细在 Redis，丢了不影响正确性）。
    /// </summary>
    public class ChatDeliveryRepository : DataAccessBase, IChatDeliveryRepository
    {
        public ChatDeliveryRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public async Task<long> MergeAsync(long tenantId, long userId, long messageId)
        {
            var row = await FindAsync(tenantId, userId).ConfigureAwait(false);

            if (row is null)
            {
                var created = new EtChatDelivery
                {
                    TenantId = tenantId,
                    UserId = userId,
                    LastDeliveredMessageId = messageId
                };

                try
                {
                    await _dbContext.InsertAsync(created).ConfigureAwait(false);
                    return messageId;
                }
                catch (Exception ex)
                {
                    // 并发首条：另一条连接刚插进去，回查后按更新走，别把 Ack 扔掉
                    _logger.Warning($"投递水位插入冲突，改为更新: tenant={tenantId} user={userId} {ex.Message}");
                    row = await FindAsync(tenantId, userId).ConfigureAwait(false);
                    if (row is null) throw;
                }
            }

            if (row.LastDeliveredMessageId >= messageId)
                return row.LastDeliveredMessageId;

            row.LastDeliveredMessageId = messageId;
            await _dbContext.UpdateAsync(row).ConfigureAwait(false);
            return messageId;
        }

        public async Task<long?> GetAsync(long tenantId, long userId)
        {
            var row = await FindAsync(tenantId, userId).ConfigureAwait(false);
            return row is null ? null : row.LastDeliveredMessageId;
        }

        private Task<EtChatDelivery?> FindAsync(long tenantId, long userId)
            => _dbContext.SingleOrDefaultAsync<EtChatDelivery>(x => x.TenantId == tenantId && x.UserId == userId);
    }
}
