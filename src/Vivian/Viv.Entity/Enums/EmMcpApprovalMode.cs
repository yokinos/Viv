using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// MCP 工具审批模式（OtMcpServer.ApprovalMode）
    /// </summary>
    public enum EmMcpApprovalMode
    {
        /// <summary>
        /// 不审
        /// </summary>
        None = 0,

        /// <summary>
        /// 全部要审
        /// </summary>
        All = 1,

        /// <summary>
        /// 按名单（AlwaysRequireToolNames / NeverRequireToolNames）
        /// </summary>
        ByList = 2
    }
}
