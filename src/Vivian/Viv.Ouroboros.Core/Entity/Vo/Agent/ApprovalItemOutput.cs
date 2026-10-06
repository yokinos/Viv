using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 待审批项
    /// </summary>
    public class ApprovalItemOutput
    {
        /// <summary>
        /// 审批单标识（对外用它批准/拒绝）
        /// </summary>
        public Guid ApprovalId { get; set; }

        /// <summary>
        /// 所属会话
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// 待审批的工具键
        /// </summary>
        public string ToolKey { get; set; } = string.Empty;

        /// <summary>
        /// 入参快照（JSON）
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 发起时间
        /// </summary>
        public DateTime? RequestedAt { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public DateTime? ExpiresAt { get; set; }
    }
}
