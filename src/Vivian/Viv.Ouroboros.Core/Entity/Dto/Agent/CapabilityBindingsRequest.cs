using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 能力绑定管理接口的新增请求
    /// </summary>
    /// <param name="AgentKey">宿主 Agent 业务键，必须已存在</param>
    /// <param name="CapabilityType">能力类型，取 EmCapabilityType</param>
    /// <param name="CapabilityKey">能力键：Tool 指向 OtTool.ToolKey，SubAgent 指向 OtAgent.AgentKey，McpServer 指向 OtMcpServer.ServerName；不存在直接报错</param>
    /// <param name="ExposedName">暴露给模型的名字，为空用能力自身的名字</param>
    /// <param name="ExposedDescription">暴露给模型的描述，为空用能力自身的描述</param>
    /// <param name="Priority">排序</param>
    /// <param name="RequiresApproval">是否覆盖能力自身的审批设置；null = 沿用能力自身</param>
    /// <param name="AllowedSubjectIds">主体白名单 JSON 数组（如 [1,2]）；空白或空数组 = 全部主体可用</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    public sealed record CreateCapabilityBindingRequest(string AgentKey, int CapabilityType, string CapabilityKey, string? ExposedName,
        string? ExposedDescription, int? Priority, bool? RequiresApproval, string? AllowedSubjectIds, bool? IsEnabled);

    /// <summary>
    /// 能力绑定管理接口的修改请求：整体覆盖；AgentKey 不在这里，改宿主等于换一条绑定
    /// </summary>
    /// <param name="Id">绑定行 Id</param>
    /// <param name="CapabilityType">能力类型，取 EmCapabilityType</param>
    /// <param name="CapabilityKey">能力键，必须指向真实存在的能力</param>
    /// <param name="ExposedName">暴露给模型的名字</param>
    /// <param name="ExposedDescription">暴露给模型的描述</param>
    /// <param name="Priority">排序</param>
    /// <param name="RequiresApproval">是否覆盖能力自身的审批设置；null = 沿用能力自身</param>
    /// <param name="AllowedSubjectIds">主体白名单 JSON 数组；空白或空数组 = 全部主体可用</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    public sealed record UpdateCapabilityBindingRequest(long Id, int CapabilityType, string CapabilityKey, string? ExposedName,
        string? ExposedDescription, int? Priority, bool? RequiresApproval, string? AllowedSubjectIds, bool? IsEnabled);
}
