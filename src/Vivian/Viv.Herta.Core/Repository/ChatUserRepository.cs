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

        /// <summary>
        /// 按「归属主体 + 登录账号」定位账号。
        /// <para>
        /// 不能按 RealmId 查：一个域里可能同时有多个主体的账号（跨主体对话），
        /// 各自主体的登录账号互不相干，域内并不唯一。
        /// </para>
        /// </summary>
        public Task<EtChatUser?> GetByLoginCodeAsync(long ownerSubjectId, string loginCode)
            => _dbContext.SingleOrDefaultAsync<EtChatUser>(x => x.OwnerSubjectId == ownerSubjectId && x.LoginCode == loginCode);

        /// <summary>
        /// 找这个归属主体当前使用的聊天域。注册时用它决定「复用已有域」还是「新开一个」，
        /// 保证同一主体不会因为反复注册而散落到多个域里。
        /// </summary>
        public Task<EtChatUser?> GetByOwnerAsync(long ownerSubjectId)
            => _dbContext.SingleOrDefaultAsync<EtChatUser>(x => x.OwnerSubjectId == ownerSubjectId);

        public async Task<long> AddAsync(EtChatUser user)
        {
            if (!await _dbContext.InsertAsync(user).ConfigureAwait(false))
                throw new InvalidOperationException("IM 用户写入失败");

            return user.Id;
        }

        public Task<bool> UpdateAsync(EtChatUser user)
            => _dbContext.UpdateAsync(user);
    }
}
