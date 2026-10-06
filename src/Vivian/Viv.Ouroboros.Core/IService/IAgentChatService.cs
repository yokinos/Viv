using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 对话：发消息（含流式）、审批与续跑、待审批列表
    /// </summary>
    public interface IAgentChatService
    {
        /// <summary>
        /// 发一条消息并跑完一轮（非流式）
        /// </summary>
        Task<VivApiResult> SendAsync(Guid conversationKey, SendMessageRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发一条消息并流式吐出增量文本（SSE 用，不套统一信封）
        /// </summary>
        IAsyncEnumerable<string> StreamAsync(Guid conversationKey, SendMessageRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 当前主体下的待审批列表
        /// </summary>
        Task<VivApiResult> ListPendingApprovalsAsync();

        /// <summary>
        /// 审批并续跑被暂停的那一轮
        /// </summary>
        /// <param name="approved">true=批准，false=拒绝；拒绝结果会作为工具结果回给模型</param>
        /// <param name="request">审批请求（内含审批单标识）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<VivApiResult> DecideApprovalAsync(bool approved, ApprovalDecisionRequest request, CancellationToken cancellationToken = default);
    }
}
