using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 子 Agent 调用状态（OtSubAgentCall.Status）
    /// </summary>
    public enum EmSubAgentCallStatus
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
        /// 超时
        /// </summary>
        TimedOut = 3,

        /// <summary>
        /// 降级返回
        /// </summary>
        Degraded = 4
    }
}
