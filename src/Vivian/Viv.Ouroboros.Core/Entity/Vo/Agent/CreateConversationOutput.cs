using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 创建会话结果
    /// </summary>
    public class CreateConversationOutput
    {
        /// <summary>
        /// 会话标识（对外唯一键）
        /// </summary>
        public Guid ConversationKey { get; set; }
    }
}
