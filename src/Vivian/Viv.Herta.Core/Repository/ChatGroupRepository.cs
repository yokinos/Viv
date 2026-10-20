using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Herta.Core.Repository
{
    public class ChatGroupRepository : DataAccessBase, IChatGroupRepository
    {
        public ChatGroupRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public async Task<long> AddAsync(EtGroupChat group)
        {
            if (!await _dbContext.InsertAsync(group).ConfigureAwait(false))
                throw new InvalidOperationException("群写入失败");

            return group.Id;
        }

        public Task<EtGroupChat?> GetAsync(long tenantId, long groupId)
            => _dbContext.SingleOrDefaultAsync<EtGroupChat>(x => x.TenantId == tenantId && x.Id == groupId);

        public async Task TouchLastMessageAsync(long tenantId, long groupId, DateTime lastMessageAt)
        {
            var group = await _dbContext.SingleOrDefaultAsync<EtGroupChat>(
                x => x.TenantId == tenantId && x.Id == groupId).ConfigureAwait(false);

            if (group == null) return;

            group.LastMessageAt = lastMessageAt;
            await _dbContext.UpdateAsync(group).ConfigureAwait(false);
        }

        public Task<List<EtGroupChat>> GetListAsync(long tenantId, List<long> groupIds)
        {
            if (groupIds.Count == 0)
                return Task.FromResult(new List<EtGroupChat>());

            return _dbContext.FindListAsync<EtGroupChat>(x => x.TenantId == tenantId && groupIds.Contains(x.Id));
        }
    }
}
