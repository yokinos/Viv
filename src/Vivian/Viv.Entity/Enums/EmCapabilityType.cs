using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// Agent 能力类型（OtCapabilityBinding.CapabilityType）
    /// </summary>
    public enum EmCapabilityType
    {
        /// <summary>
        /// 工具（能力键为 OtTool.ToolKey）
        /// </summary>
        Tool = 1,

        /// <summary>
        /// 子 Agent（能力键为 OtAgent.AgentKey）
        /// </summary>
        SubAgent = 2,

        /// <summary>
        /// MCP 服务（能力键为 OtMcpServer.ServerName）
        /// </summary>
        McpServer = 3
    }
}
