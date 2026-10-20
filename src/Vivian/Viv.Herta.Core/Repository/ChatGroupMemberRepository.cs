using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Herta.Core.Repository
{
    /// <summary>
    /// 群成员仓储。成员只在连接建立时读一次，命中不了 Cache-Aside 的收益，所以走无缓存基类。
    /// </summary>
    public class ChatGroupMemberRepository : DataAccessBase, IChatGroupMemberRepository
    {
        public ChatGroupMemberRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public async Task<List<long>> GetGroupIdsAsync(long tenantId, long userId)
        {
            var members = await GetMembershipsAsync(tenantId, userId).ConfigureAwait(false);
            return members.Select(x => x.GroupChatId).Distinct().ToList();
        }

        public Task<List<EtGroupChatMember>> GetMembershipsAsync(long tenantId, long userId)
        {
            // 软删除由全局过滤器处理，这里只看「未退群」
            return _dbContext.FindListAsync<EtGroupChatMember>(
                x => x.TenantId == tenantId && x.UserId == userId && x.QuitAt == null);
        }

        public Task<EtGroupChatMember?> GetMemberAsync(long tenantId, long groupId, long userId)
            => _dbContext.SingleOrDefaultAsync<EtGroupChatMember>(
                x => x.TenantId == tenantId && x.GroupChatId == groupId && x.UserId == userId && x.QuitAt == null);

        public Task<List<EtGroupChatMember>> GetGroupMembersAsync(long tenantId, long groupId)
            => _dbContext.FindListAsync<EtGroupChatMember>(
                x => x.TenantId == tenantId && x.GroupChatId == groupId && x.QuitAt == null);

        public async Task AddMembersAsync(List<EtGroupChatMember> members)
        {
            if (members.Count == 0) return;

            if (!await _dbContext.InsertAsync(members).ConfigureAwait(false))
                throw new InvalidOperationException("群成员写入失败");
        }

        public async Task<bool> RemoveMemberAsync(long tenantId, long groupId, long userId, DateTime quitAt)
        {
            var member = await GetMemberAsync(tenantId, groupId, userId).ConfigureAwait(false);
            if (member == null) return false;

            member.QuitAt = quitAt;
            await _dbContext.UpdateAsync(member).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> IsMemberAsync(long tenantId, long groupId, long userId)
        {
            var member = await _dbContext.SingleOrDefaultAsync<EtGroupChatMember>(
                x => x.TenantId == tenantId && x.GroupChatId == groupId && x.UserId == userId && x.QuitAt == null);

            return member != null;
        }

        public async Task SetLastReadAsync(long tenantId, long groupId, long userId, long lastReadMessageId)
        {
            var member = await _dbContext.SingleOrDefaultAsync<EtGroupChatMember>(
                x => x.TenantId == tenantId && x.GroupChatId == groupId && x.UserId == userId && x.QuitAt == null);

            // 不是成员直接返回，不插成员行
            if (member == null || lastReadMessageId <= member.LastReadMsgId) return;

            member.LastReadMsgId = lastReadMessageId;
            await _dbContext.UpdateAsync(member);
        }
    }
}
