using Viv.Entity.Database.Ouroboros;

namespace Viv.Ouroboros.Core.Entity.Model.Agent
{
    /// <summary>
    /// 某 Agent 的一条启用 MCP 服务绑定：绑定决定暴露名前缀、审批覆盖与主体白名单，
    /// 服务本体决定怎么连、暴露哪些工具。与 <see cref="AgentToolDefinition"/> 同一手法：
    /// 一次读出两半，避免"读到的绑定与配置不是同一时刻"的窗口。
    /// </summary>
    public sealed record AgentMcpServerDefinition(OtCapabilityBinding Binding, OtMcpServer Server);
}
