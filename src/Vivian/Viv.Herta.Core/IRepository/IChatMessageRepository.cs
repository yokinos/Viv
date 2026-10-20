using Viv.Entity.Enums;
using Viv.Herta.Core.Entity.Model.Chat;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>
    /// 消息仓储。单聊与群聊分表，读写都经 <see cref="ChatMessageEntry"/> 收敛。
    /// </summary>
    public interface IChatMessageRepository
    {
        /// <summary>追加一条消息，返回落库后的行Id</summary>
        Task<long> AppendAsync(ChatMessageEntry entry);

        /// <summary>按 Id 取单条消息。撤回与归属校验用</summary>
        Task<ChatMessageEntry?> GetAsync(long tenantId, long messageId, EmChatReceiverType receiverType);

        /// <summary>标记为已撤回。消息不存在返回 false</summary>
        Task<bool> RecallAsync(long tenantId, long messageId, EmChatReceiverType receiverType, DateTime recalledAt);

        /// <summary>
        /// 按对端取消息。消息属于会话，不属于某一侧的个人会话行。
        /// </summary>
        /// <param name="afterMessageId">向后增量：只取该 Id 之后，升序</param>
        /// <param name="beforeMessageId">向前翻历史：只取该 Id 之前，取最近 N 条</param>
        /// <remarks>两个都为 0 时取最新 N 条。返回一律升序。</remarks>
        Task<List<ChatMessageEntry>> ListAsync(
            long tenantId, EmChatReceiverType receiverType, long userId, long targetId,
            long afterMessageId, long beforeMessageId, int limit);

        /// <summary>该会话里发给我的、且 Id 大于已读水位的消息条数（群聊排除自己发的）</summary>
        Task<int> CountUnreadAsync(
            long tenantId, EmChatReceiverType receiverType, long userId, long targetId, long lastReadMessageId);
    }
}
