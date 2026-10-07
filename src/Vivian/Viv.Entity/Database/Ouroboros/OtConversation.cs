using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 会话头。SubjectId 取代框架的 TenantId —— 本项目的隔离维度。
    /// 注意：这些实体不实现 ITenant，框架的自动主体过滤对它们不生效，
    /// 所有查询必须显式带 SubjectId 条件，仓储层要统一封装。
    /// </summary>
    public class OtConversation : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 对外会话标识（GUID），前端只认它，不暴露自增主键
        /// </summary>
        public Guid ConversationKey { get; set; }

        /// <summary>
        /// 隔离主体（本项目的 subjectId）
        /// </summary>
        public long? SubjectId { get; set; }

        /// <summary>
        /// 终端用户Id
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// 主 Agent 业务键（OtAgent.AgentKey）
        /// </summary>
        public string MainAgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 会话标题（可由首条消息自动生成，便于列表展示）
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 会话状态，取 <see cref="EmConversationStatus"/>
        /// </summary>
        public EmConversationStatus Status { get; set; } = EmConversationStatus.Active;

        /// <summary>
        /// 消息条数（冗余计数，列表展示用）
        /// </summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// 最后一条消息时间（列表排序用）
        /// </summary>
        public DateTime? LastMessageAt { get; set; }

        /// <summary>
        /// 累计输入 token
        /// </summary>
        public long TotalInputTokens { get; set; }

        /// <summary>
        /// 累计输出 token
        /// </summary>
        public long TotalOutputTokens { get; set; }

        /// <summary>
        /// 累计估算费用
        /// </summary>
        public decimal EstimatedCost { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人Id
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
