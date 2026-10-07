using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// MCP 服务详情（管理接口用），不回请求头内容
    /// </summary>
    public class McpServerDetailOutput
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
        /// 服务描述
        /// </summary>
        public string? ServerDescription { get; set; }

        /// <summary>
        /// 允许暴露的工具名 JSON 数组
        /// </summary>
        public string? AllowedTools { get; set; }

        /// <summary>
        /// 是否已配置请求头（可能含内部令牌，只回有没有）
        /// </summary>
        public bool HasHeaders { get; set; }

        /// <summary>
        /// 审批模式，取 EmMcpApprovalMode
        /// </summary>
        public int ApprovalMode { get; set; }

        /// <summary>
        /// ByList 模式下必须审批的工具名 JSON 数组
        /// </summary>
        public string? AlwaysRequireToolNames { get; set; }

        /// <summary>
        /// ByList 模式下免审批的工具名 JSON 数组
        /// </summary>
        public string? NeverRequireToolNames { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
