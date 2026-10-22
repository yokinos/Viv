using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>
    /// 会话仓储。会话是「每用户一份」的个人索引：未读游标、置顶、免打扰、最后消息时间。
    /// 消息归属不在这里（消息按对端查），会话行丢了不影响消息正确性。
    /// </summary>
    public interface IChatSessionRepository
    {
        /// <summary>当前用户与某个对端的会话；没有就建</summary>
        Task<EtChatSession> GetOrCreateAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId);

        Task<EtChatSession?> FindAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId);

        /// <summary>
        /// 单聊两个方向的会话行一次查出来（A→B 与 B→A）。发送一条消息要拿双方会话，
        /// 分两次查就是两次往返。
        /// </summary>
        Task<List<EtChatSession>> FindDirectPairAsync(long tenantId, long userA, long userB);

        /// <summary>该用户的单聊会话行（群聊会话走群成员表，不从这里出）</summary>
        Task<List<EtChatSession>> GetDirectSessionsAsync(long tenantId, long userId);

        Task TouchAsync(long sessionId, DateTime lastMessageTime);

        /// <summary>
        /// 一次更新多行的最后消息时间。逐行 Touch 是「先按主键查再更新」两次往返，
        /// 这里直接 UPDATE，单聊的两行合并成一条语句。
        /// </summary>
        Task TouchManyAsync(IReadOnlyList<long> sessionIds, DateTime lastMessageTime);

        /// <summary>推进已读水位：只前进不后退，乱序到达的旧请求不能把未读变回来</summary>
        Task SetLastReadAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId, long lastReadMessageId);
    }
}
