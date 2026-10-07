using Viv.Entity.Database.Ouroboros;

namespace Viv.Ouroboros.Core.Entity.Model.Agent
{
    /// <summary>
    /// 某 Agent 的一条启用工具绑定：绑定决定"暴露给模型的名字/描述"与审批覆盖，工具本体决定怎么执行。
    /// 两者一起返回是因为注册表两样都要用，分两次取会出现"读到的绑定与工具不是同一时刻"的窗口。
    /// </summary>
    public sealed record AgentToolDefinition(OtCapabilityBinding Binding, OtTool Tool);
}
