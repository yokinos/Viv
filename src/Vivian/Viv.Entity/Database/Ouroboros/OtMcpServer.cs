using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// MCP 服务注册：一个 MCP server 就是一组工具的来源。
    /// 子 Agent 在契约上等价于"只暴露一个 query 工具的 MCP server"，
    /// 所以第三方 MCP 服务与自家子 Agent 可以走同一套注册与调用。
    /// </summary>
    public class OtMcpServer : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 服务名，如 apex-tools / filesystem
        /// </summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// 传输方式，取 <see cref="EmMcpTransport"/>
        /// </summary>
        public EmMcpTransport Transport { get; set; }

        /// <summary>
        /// 服务地址（HTTP/SSE 时）
        /// </summary>
        public string? ServerAddress { get; set; }

        /// <summary>
        /// 服务描述，会作为挑选依据展示给模型
        /// </summary>
        public string? ServerDescription { get; set; }

        /// <summary>
        /// 允许暴露的工具名清单（JSON 数组，空=全部）
        /// </summary>
        public string? AllowedTools { get; set; }

        /// <summary>
        /// 调用时附加的请求头（JSON 对象，如内部令牌）
        /// </summary>
        public string? Headers { get; set; }

        /// <summary>
        /// 审批模式，取 <see cref="EmMcpApprovalMode"/>
        /// </summary>
        public EmMcpApprovalMode ApprovalMode { get; set; }

        /// <summary>
        /// 审批模式取 <see cref="EmMcpApprovalMode.ByList"/> 时，必须审批的工具名清单（JSON 数组）
        /// </summary>
        public string? AlwaysRequireToolNames { get; set; }

        /// <summary>
        /// 审批模式取 <see cref="EmMcpApprovalMode.ByList"/> 时，免审批的工具名清单（JSON 数组）
        /// </summary>
        public string? NeverRequireToolNames { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人Id
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
