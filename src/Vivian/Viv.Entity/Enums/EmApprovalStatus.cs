using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 人工审批单状态（OtApproval.Status）
    /// </summary>
    public enum EmApprovalStatus
    {
        /// <summary>
        /// 待审批
        /// </summary>
        Pending = 1,

        /// <summary>
        /// 已批准
        /// </summary>
        Approved = 2,

        /// <summary>
        /// 已拒绝
        /// </summary>
        Rejected = 3,

        /// <summary>
        /// 已超时
        /// </summary>
        TimedOut = 4
    }
}
