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
        /// 受理一轮并流式返回（队列路径）：落库 + 投递事件后等 Worker 跑完，推出最终文本；
        /// 客户端断开不影响那一轮继续执行（这是切队列的意义）。
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="request">消息内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        IAsyncEnumerable<string> StreamQueuedTurnAsync(Guid conversationKey, SendMessageRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 跑一条**已落库**的用户消息对应的那一轮（消息队列消费用）：不再新建用户消息，只补助手回复与会话快照
        /// </summary>
        /// <param name="conversationKey">会话标识</param>
        /// <param name="userMessageId">已落库的用户消息 Id（OtMessage.Id）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<VivApiResult> RunQueuedTurnAsync(Guid conversationKey, long userMessageId, CancellationToken cancellationToken = default);

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
