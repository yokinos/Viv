using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 会话列表项
    /// </summary>
    public class ConversationItemOutput
    {
        /// <summary>
        /// 会话标识
        /// </summary>
        public Guid ConversationKey { get; set; }

        /// <summary>
        /// 主 Agent 业务键
        /// </summary>
        public string MainAgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 会话标题
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 状态：1=进行中 2=已结束
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 消息条数
        /// </summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// 最后一条消息时间
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
    }
}
