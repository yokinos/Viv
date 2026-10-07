using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// MCP 服务传输方式（OtMcpServer.Transport）
    /// </summary>
    public enum EmMcpTransport
    {
        /// <summary>
        /// HTTP(SSE) 接入
        /// </summary>
        HttpSse = 1,

        /// <summary>
        /// stdio 子进程
        /// </summary>
        Stdio = 2,

        /// <summary>
        /// 进程内（自家子 Agent 走这条）
        /// </summary>
        InProcess = 3
    }
}
