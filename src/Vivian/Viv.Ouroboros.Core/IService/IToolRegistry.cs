using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 工具注册表：把库里 OtCapabilityBinding + OtTool 定义的启用工具装配成模型可见的
    /// <see cref="AITool"/> 列表，交给 AgentFactory 塞进 ChatOptions.Tools。
    /// 没绑定的工具模型看不到，也就调不到 —— 这是权限边界生效的地方。
    /// </summary>
    public interface IToolRegistry
    {
        /// <summary>
        /// 取该 Agent 可用的工具。类型不支持（MCP 等）或内置实现缺失的工具会被跳过并记 Warning，
        /// 不影响其余工具 —— 一个配错的工具不该让整个 Agent 装不出来。
        /// </summary>
        Task<IList<AITool>> GetToolsAsync(string agentKey);

        /// <summary>清掉某个 Agent 的工具缓存（改完绑定或工具定义后调用）</summary>
        void Invalidate(string agentKey);

        /// <summary>清掉全部 Agent 的工具缓存</summary>
        void InvalidateAll();

        /// <summary>
        /// 该 Agent 的工具列表是否随主体变（有"需审批 + 配了主体白名单"的绑定才会）。
        /// AgentFactory 的缓存把工具嵌在 Agent 里，得跟着一起按主体分键，所以要有这个出口。
        /// </summary>
        bool IsSubjectScoped(string agentKey);

        /// <summary>
        /// 该 Agent 是否有"需审批"的能力绑定。有的话"套不套审批壳"取决于库里的主体白名单，
        /// 而壳是装配期定的，所以这类 Agent 的装配缓存只能短时复用（AgentFactory 也得跟着短缓存）。
        /// </summary>
        bool IsApprovalSensitive(string agentKey);
    }
}
