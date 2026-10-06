using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 会话详情（会话头 + 最近若干条消息）
    /// </summary>
    public class ConversationDetailOutput
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
        /// 最近的消息
        /// </summary>
        public List<MessageItemOutput> Messages { get; set; } = [];
    }
}
