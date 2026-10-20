using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Herta.Core.Repository
{
    public class ChatUserRepository : DataAccessBase, IChatUserRepository
    {
        public ChatUserRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public Task<EtChatUser?> GetByLoginCodeAsync(long tenantId, string loginCode)
            => _dbContext.SingleOrDefaultAsync<EtChatUser>(x => x.TenantId == tenantId && x.LoginCode == loginCode);

        public async Task<long> AddAsync(EtChatUser user)
        {
            if (!await _dbContext.InsertAsync(user).ConfigureAwait(false))
                throw new InvalidOperationException("IM 用户写入失败");

            return user.Id;
        }
    }
}
