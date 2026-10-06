using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Database.Ouroboros;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// Agent 相关的库访问入口：会话、消息、会话状态快照、审批单。
    /// 四个聚合放一起是因为它们永远一起被读写（一轮对话要同时落消息、更新会话、存快照），
    /// 拆成四个仓储只会让上层多注入三次。
    /// </summary>
    public interface IAgentStore
    {
        #region 会话

        Task<OtConversation?> GetConversationAsync(Guid conversationKey);

        /// <summary>按主体/用户列出会话，按最后消息时间倒序</summary>
        Task<List<OtConversation>> ListConversationsAsync(long? subjectId, long? userId, int pageIndex, int pageSize);

        Task<bool> InsertConversationAsync(OtConversation conversation);

        Task<bool> UpdateConversationAsync(OtConversation conversation);

        #endregion

        #region 消息

        /// <summary>取 seq 大于 afterSeq 的消息，按 seq 升序；limit &lt;= 0 表示不限</summary>
        Task<List<OtMessage>> ListMessagesAsync(long conversationId, int afterSeq = 0, int limit = 0);

        /// <summary>取该会话下一个可用 Seq</summary>
        Task<int> NextSeqAsync(long conversationId);

        Task<bool> InsertMessageAsync(OtMessage message);

        #endregion

        #region 会话状态快照

        Task<OtAgentSessionState?> GetSessionStateAsync(long conversationId, string agentKey);

        /// <summary>有则更新、无则插入</summary>
        Task<bool> SaveSessionStateAsync(long conversationId, string agentKey, string stateJson);

        #endregion

        #region 审批

        Task<bool> InsertApprovalAsync(OtApproval approval);

        Task<OtApproval?> GetApprovalAsync(Guid approvalId);

        /// <summary>待审批列表（状态 1），按发起时间升序</summary>
        Task<List<OtApproval>> ListPendingApprovalsAsync(long? subjectId);

        Task<bool> UpdateApprovalAsync(OtApproval approval);

        #endregion
    }
}
