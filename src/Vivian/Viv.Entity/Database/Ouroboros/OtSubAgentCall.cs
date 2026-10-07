using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 子 Agent 调用留痕：主 Agent 每调一次子 Agent 就是一次完整的嵌套会话，
    /// 成本与耗时必须单独看得见，否则说不清钱花在哪个域上。
    /// </summary>
    public class OtSubAgentCall : EntityBase, ICreatedAt
    {
        /// <summary>
        /// 所属会话（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// 主 Agent 那条触发调用的用户消息（OtMessage.Id）。子 Agent 回调发生在助手消息落库之前，那一刻助手消息还不存在；审批续跑那一轮为空
        /// </summary>
        public long? ParentMessageId { get; set; }

        /// <summary>
        /// 调用方主 Agent 业务键
        /// </summary>
        public string? CallerAgentKey { get; set; }

        /// <summary>
        /// 被调用的子 Agent 业务键（OtAgent.AgentKey）
        /// </summary>
        public string SubAgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 子 Agent 归属域 —— 跨进程调用时就是那个服务
        /// </summary>
        public string? OwnerDomain { get; set; }

        /// <summary>
        /// 传给子 Agent 的任务描述
        /// </summary>
        public string? Task { get; set; }

        /// <summary>
        /// 子 Agent 回的结论（只回结论，不回推理过程）
        /// </summary>
        public string? ResultSummary { get; set; }

        /// <summary>
        /// 调用状态，取 <see cref="EmSubAgentCallStatus"/>
        /// </summary>
        public EmSubAgentCallStatus Status { get; set; }

        /// <summary>
        /// 子 Agent 会话消耗的输入 token
        /// </summary>
        public int? InputTokens { get; set; }

        /// <summary>
        /// 子 Agent 会话消耗的输出 token
        /// </summary>
        public int? OutputTokens { get; set; }

        /// <summary>
        /// 调用耗时（毫秒）
        /// </summary>
        public int? LatencyMs { get; set; }

        /// <summary>
        /// 失败原因
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}
