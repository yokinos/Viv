using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 工具调用留痕。IdempotencyKey 唯一：模型会重复调同一个写工具，靠它保证只生效一次。
    /// </summary>
    public class OtToolCall : EntityBase, ICreatedAt
    {
        /// <summary>
        /// 所属会话（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// 触发这次调用的助手消息（OtMessage.Id）
        /// </summary>
        public long? MessageId { get; set; }

        /// <summary>
        /// 调用方 Agent（主 Agent 或子 Agent 的业务键）
        /// </summary>
        public string? AgentKey { get; set; }

        /// <summary>
        /// 工具键（OtTool.ToolKey）
        /// </summary>
        public string ToolKey { get; set; } = string.Empty;

        /// <summary>
        /// 入参 JSON（模型给的原始参数，审计与复现用）
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 结果摘要（截断后的，避免把大结果整段塞进上下文与库）
        /// </summary>
        public string? ResultSummary { get; set; }

        /// <summary>
        /// 调用状态：1=成功 2=失败 3=待审批 4=被拒 5=超时
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 本次调用是否走了人工审批
        /// </summary>
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// 关联审批单（OtApproval.Id）
        /// </summary>
        public long? ApprovalId { get; set; }

        /// <summary>
        /// 调用耗时（毫秒）
        /// </summary>
        public int? LatencyMs { get; set; }

        /// <summary>
        /// 幂等键：调用方 + 工具 + 入参哈希，唯一约束防重复生效
        /// </summary>
        public string? IdempotencyKey { get; set; }

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
