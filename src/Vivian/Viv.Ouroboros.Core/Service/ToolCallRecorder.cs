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
    /// 工具调用留痕（OtToolCall）。
    ///
    /// 每次落库都**新开一个 DI 作用域**：装配好的 Agent（连同工具闭包）会被 AgentFactory 缓存 60 秒、
    /// 跨请求复用，闭包里绝不能持有装配那一刻的 Scoped 仓储 —— 那个作用域早就释放了。
    ///
    /// 会话上下文从 <see cref="AgentTurnContext.Current"/> 读：装配期拿不到 conversationId，
    /// 由 AgentChatService 在跑一轮时用 AsyncLocal 作用域喂进来，随 MAF 的工具回调流到这里。
    /// </summary>
    public class ToolCallRecorder
    {
        /// <summary>结果摘要与错误信息落库前的截断长度（避免把大结果整段塞进库）</summary>
        private const int TextLimit = 2000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scopeFactory">作用域工厂（Singleton，可以安全地被缓存住的工具闭包持有）</param>
        /// <param name="logger">日志</param>
        public ToolCallRecorder(IServiceScopeFactory scopeFactory, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 落一条留痕。自身失败只记日志 —— 留痕不该把工具调用（乃至整轮对话）打死。
        /// </summary>
        /// <param name="agentKey">调用方 Agent 业务键</param>
        /// <param name="toolKey">工具键</param>
        /// <param name="requiresApproval">该工具是否走了人工审批</param>
        /// <param name="arguments">入参 JSON</param>
        /// <param name="result">结果正文（可为 null）</param>
        /// <param name="status">调用状态，取 <see cref="EmToolCallStatus"/></param>
        /// <param name="latencyMs">耗时毫秒</param>
        /// <param name="errorMessage">失败原因</param>
        public async Task WriteAsync(string agentKey, string toolKey, bool requiresApproval, string? arguments,
            string? result, EmToolCallStatus status, int latencyMs, string? errorMessage)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetService<IAgentStore>();
                if (store is null)
                {
                    _logger.Warning("工具调用留痕跳过：解析不到 IAgentStore");
                    return;
                }

                await store.InsertToolCallAsync(new OtToolCall
                {
                    // 不在跑轮期间（直连自检接口等）拿不到会话，退回 0/null —— 与改动前一致
                    ConversationId = AgentTurnContext.Current?.ConversationId ?? 0,
                    MessageId = AgentTurnContext.Current?.MessageId,
                    AgentKey = agentKey,
                    ToolKey = toolKey,
                    Arguments = Truncate(arguments),
                    ResultSummary = Truncate(result),
                    Status = status,
                    RequiresApproval = requiresApproval,
                    ApprovalId = null,
                    LatencyMs = latencyMs,
                    // 幂等键的语义是"同一轮里同一个调用只生效一次"，这层认不出"同一轮"，留空比编一个更诚实
                    IdempotencyKey = null,
                    ErrorMessage = Truncate(errorMessage)
                });
            }
            catch (Exception ex)
            {
                _logger.Error("工具调用留痕入库失败：{0}，{1}", toolKey, ex.Message);
            }
        }

        /// <summary>入参序列化成 JSON。值可能是 JsonElement（模型来的）也可能是强类型对象，统一走序列化。</summary>
        public static string? SerializeArguments(AIFunctionArguments? arguments)
        {
            if (arguments is null || arguments.Count == 0) return null;

            var payload = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
            foreach (var pair in arguments) payload[pair.Key] = pair.Value;

            try
            {
                return JsonSerializer.Serialize(payload);
            }
            catch (Exception)
            {
                // 值里有不可序列化的东西（第三方对象、循环引用）时不要让留痕变成故障点
                return null;
            }
        }

        private static string? Truncate(string? text)
            => string.IsNullOrEmpty(text) || text.Length <= TextLimit ? text : text[..TextLimit];
    }
}
