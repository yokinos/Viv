using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 子 Agent 工具的实体：不用 MAF 的 <c>AsAIFunction</c>（它只回字符串，拿不到 <c>Usage</c>，
    /// OtSubAgentCall 的 InputTokens/OutputTokens 就只能永远是 null），而是自己跑子 Agent 的 <c>RunAsync</c>。
    ///
    /// 对外行为与 AsAIFunction 保持一致：名字/描述按绑定的暴露名，唯一必填入参是字符串 query，
    /// 结果仍是子 Agent 的文本（用量装在 <see cref="SubAgentRunOutcome"/> 里，只给外层留痕用，不进模型上下文）。
    /// </summary>
    public sealed class SubAgentRunnerFunction : AIFunction
    {
        /// <summary>
        /// 与 AsAIFunction 逐字节同形（实测它就是这个），模型侧的工具定义不能因为这次改动变差
        /// </summary>
        private static readonly JsonElement QuerySchema = JsonDocument.Parse("""
            {"type":"object","properties":{"query":{"description":"Input query to invoke the agent.","type":"string"}},"required":["query"]}
            """).RootElement.Clone();

        /// <summary>对模型而言结果就是文本（用量在外层拆掉），别让它看见信封的结构</summary>
        private static readonly JsonElement StringReturnSchema =
            JsonDocument.Parse("""{"type":"string"}""").RootElement.Clone();

        private readonly AIAgent _agent;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="agent">被包装的子 Agent（装配期已建好，可跨请求复用）</param>
        /// <param name="name">暴露给模型的名字（绑定上的 ExposedName 优先）</param>
        /// <param name="description">暴露给模型的描述</param>
        public SubAgentRunnerFunction(AIAgent agent, string name, string description)
        {
            _agent = agent;
            Name = name;
            Description = description;
        }

        /// <inheritdoc />
        public override string Name { get; }

        /// <inheritdoc />
        public override string Description { get; }

        /// <inheritdoc />
        public override JsonElement JsonSchema => QuerySchema;

        /// <inheritdoc />
        public override JsonElement? ReturnJsonSchema => StringReturnSchema;

        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var query = SubAgentCallRecorder.ExtractQuery(arguments) ?? string.Empty;

            // 每次调用起一个新会话：不复用主 Agent 的 session，也不跨调用记忆（子 Agent 的结论由主 Agent 转述）。
            // 会话是纯状态对象（MAF 1.23 的 AgentSession 没有 Dispose 成员），只被本次调用持有，
            // 出了本方法就不可达 —— 不挂在被缓存 60 秒的工具闭包上，也就不会跨请求泄漏
            var session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            var response = await _agent.RunAsync(query, session, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new SubAgentRunOutcome(response.Text, response.Usage);
        }
    }

    /// <summary>
    /// 子 Agent 一次调用的结果：Text 回给模型，用量只用于 OtSubAgentCall 留痕与 token 用量聚合，
    /// 不进模型上下文。
    /// </summary>
    /// <param name="Text">子 Agent 的结论正文</param>
    /// <param name="Usage">子 Agent 那几次模型调用的用量；供应商不给时为 null</param>
    public sealed record SubAgentRunOutcome(string Text, UsageDetails? Usage);
}
