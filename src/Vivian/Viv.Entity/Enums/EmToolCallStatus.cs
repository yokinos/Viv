using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 工具调用状态（OtToolCall.Status）
    /// </summary>
    public enum EmToolCallStatus
    {
        /// <summary>
        /// 成功
        /// </summary>
        Succeeded = 1,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 2,

        /// <summary>
        /// 待审批
        /// </summary>
        PendingApproval = 3,

        /// <summary>
        /// 被拒
        /// </summary>
        Rejected = 4,

        /// <summary>
        /// 超时
        /// </summary>
        TimedOut = 5
    }
}
