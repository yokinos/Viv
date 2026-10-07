using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// MCP 服务列表项（管理接口用），不回请求头内容
    /// </summary>
    public class McpServerItemOutput
    {
        /// <summary>
        /// MCP 服务行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 服务名
        /// </summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// 传输方式，取 EmMcpTransport
        /// </summary>
        public int Transport { get; set; }

        /// <summary>
        /// 服务地址
        /// </summary>
        public string? ServerAddress { get; set; }

        /// <summary>
        /// 审批模式，取 EmMcpApprovalMode
        /// </summary>
        public int ApprovalMode { get; set; }

        /// <summary>
        /// 是否已配置请求头（可能含内部令牌，只回有没有）
        /// </summary>
        public bool HasHeaders { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}
