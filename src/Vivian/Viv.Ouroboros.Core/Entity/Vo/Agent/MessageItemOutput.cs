using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 消息项
    /// </summary>
    public class MessageItemOutput
    {
        /// <summary>
        /// 会话内序号
        /// </summary>
        public int Seq { get; set; }

        /// <summary>
        /// 角色：user / assistant / tool / system
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// 正文
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 产生该消息的 Agent
        /// </summary>
        public string? AgentKey { get; set; }

        /// <summary>
        /// 输入 token
        /// </summary>
        public int? InputTokens { get; set; }

        /// <summary>
        /// 输出 token
        /// </summary>
        public int? OutputTokens { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}
