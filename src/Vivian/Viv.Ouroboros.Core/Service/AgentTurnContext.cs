using System;
using System.Threading;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 当前回合的会话上下文（AsyncLocal 承载）。
    ///
    /// 工具闭包由 AgentFactory 缓存 60 秒、跨请求复用，装配期根本不知道 conversationId，
    /// 也不该为此把会话串成参数穿过 AgentFactory/ToolRegistry 的装配链；所以由
    /// <see cref="AgentChatService"/> 在**跑一轮的前后**用 <see cref="Enter"/> 的 using 作用域喂进来，
    /// <see cref="ToolCallRecorder"/> / <see cref="SubAgentCallRecorder"/> 落库时读 <see cref="Current"/>。
    ///
    /// 本类只承载"这一轮是谁在跑"，租户身份仍归 IVivContextAccessor 唯一持有，不重复一份。
    /// </summary>
    public sealed class AgentTurnContext : IDisposable
    {
        private static readonly AsyncLocal<AgentTurnContext?> CurrentHolder = new();

        private readonly AgentTurnContext? _previous;
        private bool _disposed;

        private AgentTurnContext(long conversationId, long? messageId, string mainAgentKey, AgentTurnContext? previous)
        {
            ConversationId = conversationId;
            MessageId = messageId;
            MainAgentKey = mainAgentKey;
            _previous = previous;
        }

        /// <summary>
        /// 当前回合上下文；不在跑轮期间为 null
        /// </summary>
        public static AgentTurnContext? Current => CurrentHolder.Value;

        /// <summary>
        /// 会话 Id（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; }

        /// <summary>
        /// 触发本回合的消息 Id（OtMessage.Id）：Send 是本回合的用户消息，审批续跑拿不到
        /// </summary>
        public long? MessageId { get; }

        /// <summary>
        /// 本回合的主 Agent 业务键
        /// </summary>
        public string MainAgentKey { get; }

        /// <summary>
        /// 进入一个回合：设置当前上下文，Dispose 时还原到进入前的值（可嵌套）
        /// </summary>
        /// <param name="conversationId">会话 Id</param>
        /// <param name="messageId">触发本回合的消息 Id（没有就传 null）</param>
        /// <param name="mainAgentKey">主 Agent 业务键</param>
        public static AgentTurnContext Enter(long conversationId, long? messageId, string mainAgentKey)
        {
            var scope = new AgentTurnContext(conversationId, messageId, mainAgentKey, CurrentHolder.Value);
            CurrentHolder.Value = scope;
            return scope;
        }

        /// <summary>
        /// 退出回合：还原（而不是清空）—— 嵌套调用时回到外层的上下文
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CurrentHolder.Value = _previous;
        }
    }
}
