using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// Agent 管理接口的新增请求
    /// </summary>
    /// <param name="AgentKey">业务键，全局唯一且不可改（能力绑定、提示词、会话都按它引用）</param>
    /// <param name="AgentType">Agent 类型，取 EmAgentType</param>
    /// <param name="OwnerDomain">归属域：ouroboros / apex / herta / deepred / sakumai</param>
    /// <param name="DisplayName">展示名称</param>
    /// <param name="Description">什么时候该用这个 Agent（主 Agent 选子 Agent 就靠这句话）</param>
    /// <param name="ActivePromptVersion">生效的提示词版本；大于 0 时必须已存在该版本</param>
    /// <param name="ModelProfile">模型档位键，为空按 sub</param>
    /// <param name="MaxToolIterations">工具回环上限，为空按 8</param>
    /// <param name="Endpoint">跨进程执行地址（子 Agent 用）</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record CreateAgentRequest(string AgentKey, int AgentType, string OwnerDomain, string? DisplayName, string? Description,
        int? ActivePromptVersion, string? ModelProfile, int? MaxToolIterations, string? Endpoint, bool? IsEnabled, string? Remark);

    /// <summary>
    /// Agent 管理接口的修改请求：整体覆盖；AgentKey 不在这里，改键会让绑定与提示词集体悬空
    /// </summary>
    /// <param name="Id">Agent 行 Id</param>
    /// <param name="AgentType">Agent 类型，取 EmAgentType</param>
    /// <param name="OwnerDomain">归属域</param>
    /// <param name="DisplayName">展示名称</param>
    /// <param name="Description">什么时候该用这个 Agent</param>
    /// <param name="ActivePromptVersion">生效的提示词版本；null = 不改动，大于 0 时必须已存在该版本</param>
    /// <param name="ModelProfile">模型档位键</param>
    /// <param name="MaxToolIterations">工具回环上限，为空按 8</param>
    /// <param name="Endpoint">跨进程执行地址</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record UpdateAgentRequest(long Id, int AgentType, string OwnerDomain, string? DisplayName, string? Description,
        int? ActivePromptVersion, string? ModelProfile, int? MaxToolIterations, string? Endpoint, bool? IsEnabled, string? Remark);
}
