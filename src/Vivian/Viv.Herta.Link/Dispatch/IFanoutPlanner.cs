using Viv.EventContracts.Herta;

namespace Viv.Herta.Link.Dispatch
{
    public enum FanoutKind
    {
        /// <summary>单聊：查本进程连接表，按 connectionId 投递</summary>
        User = 0,

        /// <summary>群聊：投给本进程的 SignalR 组</summary>
        Group = 1
    }

    public readonly record struct FanoutDecision(FanoutKind Kind, string Target);

    /// <summary>寻址：纯函数，单独拆出来是为了不起 Hub 夹具就能覆盖各种组合。</summary>
    public interface IFanoutPlanner
    {
        /// <param name="tenantId">来自信封上下文，不取事件体</param>
        FanoutDecision Plan(SendMessageEvent evt, long tenantId);
    }
}
