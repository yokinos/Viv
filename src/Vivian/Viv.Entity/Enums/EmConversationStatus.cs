using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 会话状态（OtConversation.Status）
    /// </summary>
    public enum EmConversationStatus
    {
        /// <summary>
        /// 进行中
        /// </summary>
        Active = 1,

        /// <summary>
        /// 已结束
        /// </summary>
        Closed = 2,

        /// <summary>
        /// 挂起待审批
        /// </summary>
        Suspended = 3
    }
}
