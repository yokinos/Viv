using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 一轮对话结果
    /// </summary>
    public class ChatTurnOutput
    {
        /// <summary>
        /// 助手回复正文
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// 本轮输入 token
        /// </summary>
        public int? InputTokens { get; set; }

        /// <summary>
        /// 本轮输出 token
        /// </summary>
        public int? OutputTokens { get; set; }

        /// <summary>
        /// 本轮产生待审批时的审批单标识（注意：不是 TraceId）
        /// </summary>
        public Guid? PendingApprovalId { get; set; }
    }
}
