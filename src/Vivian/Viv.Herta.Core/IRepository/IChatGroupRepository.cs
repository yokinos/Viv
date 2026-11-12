using Viv.Entity.Database.Herta;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>
    /// 群仓储。群聊的最后消息时间是群级属性，所有成员共用一条记录。
    /// </summary>
    public interface IChatGroupRepository
    {
        /// <summary>建群，返回群Id</summary>
        Task<long> AddAsync(EtGroupChat group);

        Task<EtGroupChat?> GetAsync(long realmId, long groupId);

        Task TouchLastMessageAsync(long realmId, long groupId, DateTimeOffset lastMessageAt);

        Task<List<EtGroupChat>> GetListAsync(long realmId, List<long> groupIds);
    }
}
