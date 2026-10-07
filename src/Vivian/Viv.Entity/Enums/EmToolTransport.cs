using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 工具传输方式（OtTool.Transport）
    /// </summary>
    public enum EmToolTransport
    {
        /// <summary>
        /// 进程内方法（内置工具，按 ToolKey 查内置注册表）
        /// </summary>
        Builtin = 1,

        /// <summary>
        /// HTTP 接口
        /// </summary>
        Http = 2,

        /// <summary>
        /// MCP 工具
        /// </summary>
        Mcp = 3
    }
}
