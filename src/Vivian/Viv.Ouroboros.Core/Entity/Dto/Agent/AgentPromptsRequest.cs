using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 提示词管理接口的新增请求：只落一个新版本，不自动切换生效版本
    /// </summary>
    /// <param name="AgentKey">Agent 业务键，必须已存在</param>
    /// <param name="Version">版本号；为空或 0 时取该 AgentKey 当前最大版本 + 1</param>
    /// <param name="Content">系统提示词正文</param>
    /// <param name="Notes">这台 Agent 自己的补充说明</param>
    /// <param name="Remark">变更备注：这一版改了什么、为什么改</param>
    public sealed record CreateAgentPromptRequest(string AgentKey, int? Version, string Content, string? Notes, string? Remark);

    /// <summary>
    /// 提示词管理接口的修改请求：只改正文与备注，版本号与 AgentKey 都不可改
    /// </summary>
    /// <param name="Id">提示词行 Id</param>
    /// <param name="Content">系统提示词正文</param>
    /// <param name="Notes">补充说明</param>
    /// <param name="Remark">变更备注</param>
    public sealed record UpdateAgentPromptRequest(long Id, string Content, string? Notes, string? Remark);

    /// <summary>
    /// 切换 Agent 生效提示词版本的请求
    /// </summary>
    /// <param name="AgentKey">Agent 业务键</param>
    /// <param name="Version">要生效的版本号，必须已存在</param>
    public sealed record ActivateAgentPromptRequest(string AgentKey, int Version);
}
