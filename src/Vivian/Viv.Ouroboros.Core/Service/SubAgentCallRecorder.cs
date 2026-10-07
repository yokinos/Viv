using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 子 Agent 调用留痕（OtSubAgentCall）。与 <see cref="ToolCallRecorder"/> 同一手法：
    /// 每次落库新开一个 DI 作用域，因为持有本记录器的子 Agent 工具闭包会被 AgentFactory 缓存 60 秒。
    /// 会话上下文从 <see cref="AgentTurnContext.Current"/> 读（由 AgentChatService 在跑一轮时喂进来）；
    /// token 用量由子 Agent 的 AIFunction 自己跑 <c>RunAsync</c> 后从 <c>AgentResponse.Usage</c> 传进来。
    /// </summary>
    public class SubAgentCallRecorder
    {
        /// <summary>任务与结论落库前的截断长度（避免把整段子会话塞进库）</summary>
        private const int TextLimit = 2000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scopeFactory">作用域工厂（Singleton，可被缓存住的工具闭包安全持有）</param>
        /// <param name="logger">日志</param>
        public SubAgentCallRecorder(IServiceScopeFactory scopeFactory, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 落一条子 Agent 调用留痕。自身失败只记日志 —— 留痕不该把子 Agent 调用乃至整轮对话打死。
        /// </summary>
        /// <param name="callerAgentKey">调用方主 Agent 业务键</param>
        /// <param name="subAgentKey">被调用的子 Agent 业务键</param>
        /// <param name="ownerDomain">子 Agent 归属域</param>
        /// <param name="task">传给子 Agent 的任务描述</param>
        /// <param name="result">子 Agent 回的结论</param>
        /// <param name="status">调用状态，取 <see cref="EmSubAgentCallStatus"/></param>
        /// <param name="latencyMs">耗时毫秒</param>
        /// <param name="errorMessage">失败原因</param>
        /// <param name="inputTokens">子 Agent 会话消耗的输入 token（拿不到就传 null）</param>
        /// <param name="outputTokens">子 Agent 会话消耗的输出 token（拿不到就传 null）</param>
        public async Task WriteAsync(string callerAgentKey, string subAgentKey, string? ownerDomain, string? task,
            string? result, EmSubAgentCallStatus status, int latencyMs, string? errorMessage,
            int? inputTokens = null, int? outputTokens = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetService<IAgentStore>();
                if (store is null)
                {
                    _logger.Warning("子 Agent 调用留痕跳过：解析不到 IAgentStore");
                    return;
                }

                await store.InsertSubAgentCallAsync(new OtSubAgentCall
                {
                    // 不在跑轮期间（直连自检接口等）拿不到会话，退回 0/null —— 与改动前一致
                    ConversationId = AgentTurnContext.Current?.ConversationId ?? 0,
                    ParentMessageId = AgentTurnContext.Current?.MessageId,
                    CallerAgentKey = callerAgentKey,
                    SubAgentKey = subAgentKey,
                    OwnerDomain = ownerDomain,
                    Task = Truncate(task),
                    ResultSummary = Truncate(result),
                    Status = status,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    LatencyMs = latencyMs,
                    ErrorMessage = Truncate(errorMessage)
                });
            }
            catch (Exception ex)
            {
                _logger.Error("子 Agent 调用留痕入库失败：{0}，{1}", subAgentKey, ex.Message);
            }
        }

        /// <summary>取子 Agent 工具的唯一入参 query；拿不到时退回整个入参的序列化文本</summary>
        public static string? ExtractQuery(AIFunctionArguments? arguments)
        {
            if (arguments is null || arguments.Count == 0) return null;

            if (arguments.TryGetValue("query", out var query) && query is not null)
            {
                return query is JsonElement element
                    ? (element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText())
                    : query.ToString();
            }

            return ToolCallRecorder.SerializeArguments(arguments);
        }

        private static string? Truncate(string? text)
            => string.IsNullOrEmpty(text) || text.Length <= TextLimit ? text : text[..TextLimit];
    }
}
