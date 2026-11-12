using Viv.Entity.Database.Herta;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>
    /// 群成员仓储。实时层靠它回答「这个用户该收到哪些群的消息」。
    /// </summary>
    public interface IChatGroupMemberRepository
    {
        /// <summary>该用户当前所在的群Id（已退群、已删除的不算）</summary>
        Task<List<long>> GetGroupIdsAsync(long realmId, long userId);

        /// <summary>该用户当前的群成员行（含置顶/免打扰/已读游标）</summary>
        Task<List<EtGroupChatMember>> GetMembershipsAsync(long realmId, long userId);

        /// <summary>群在某用户视角下的成员行；已退群返回 null</summary>
        Task<EtGroupChatMember?> GetMemberAsync(long realmId, long groupId, long userId);

        /// <summary>群的全部在群成员</summary>
        Task<List<EtGroupChatMember>> GetGroupMembersAsync(long realmId, long groupId);

        /// <summary>批量写成员行</summary>
        Task AddMembersAsync(List<EtGroupChatMember> members);

        /// <summary>把成员标记为已退群。不在群里返回 false</summary>
        Task<bool> RemoveMemberAsync(long realmId, long groupId, long userId, DateTimeOffset quitAt);

        Task<bool> IsMemberAsync(long realmId, long groupId, long userId);

        /// <summary>推进群已读水位：只前进不后退，不是成员直接返回</summary>
        Task SetLastReadAsync(long realmId, long groupId, long userId, long lastReadMessageId);
    }
}
