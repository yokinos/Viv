using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// Agent 能力绑定 = 权限边界。
    /// 主 Agent 绑子 Agent，子 Agent 绑工具/MCP 工具；运行时按本表构造 ChatOptions.Tools，
    /// 没绑的能力模型根本看不到，也就不可能被调用 —— 这是"记录谁能用多少工具"真正生效的地方。
    /// </summary>
    public class OtCapabilityBinding : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 宿主 Agent 的业务键（OtAgent.AgentKey）
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 能力类型：1=工具 2=子 Agent 3=MCP 服务
        /// </summary>
        public int CapabilityType { get; set; }

        /// <summary>
        /// 能力键：工具为 OtTool.ToolKey，子 Agent 为 OtAgent.AgentKey，MCP 为 OtMcpServer.ServerName
        /// </summary>
        public string CapabilityKey { get; set; } = string.Empty;

        /// <summary>
        /// 暴露给模型的名字（可覆盖能力自身的名字，避免重名）
        /// </summary>
        public string? ExposedName { get; set; }

        /// <summary>
        /// 暴露给模型的描述（覆盖能力自身描述）
        /// </summary>
        public string? ExposedDescription { get; set; }

        /// <summary>
        /// 排序，影响模型看到的工具顺序
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否覆盖能力自身的审批设置（为空则沿用能力自身设置）
        /// </summary>
        public bool? RequiresApproval { get; set; }

        /// <summary>
        /// 授权主体白名单（JSON 数组，空=全部主体可用）—— subjectId 级别的授权
        /// </summary>
        public string? AllowedSubjectIds { get; set; }

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
