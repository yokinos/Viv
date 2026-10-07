using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Viv.Entity.Enums;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 子 Agent 工具的 <see cref="AIFunction"/> 外壳：套在 <see cref="SubAgentRunnerFunction"/> 外面，
    /// 调用前后落一行 <c>OtSubAgentCall</c>（谁调的、调的谁、任务、结论、token 用量、成败、耗时），
    /// 并把这次调用的用量累加进 <c>OtTokenUsageDaily</c>（按子 Agent 的维度单独出账）。
    /// 结果只把正文交回模型 —— 子 Agent 的回话就是模型该看到的工具结果，用量不进模型上下文。
    /// </summary>
    public sealed class SubAgentToolFunction : DelegatingAIFunction
    {
        private readonly SubAgentCallRecorder _recorder;
        private readonly TokenUsageRecorder _usage;
        private readonly string _callerAgentKey;
        private readonly string _subAgentKey;
        private readonly string? _ownerDomain;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="inner">子 Agent 的实体函数（<see cref="SubAgentRunnerFunction"/>，唯一入参 query）</param>
        /// <param name="recorder">子 Agent 调用留痕器</param>
        /// <param name="usage">token 用量日聚合写入器</param>
        /// <param name="callerAgentKey">调用方主 Agent 业务键</param>
        /// <param name="subAgentKey">被调用的子 Agent 业务键</param>
        /// <param name="ownerDomain">子 Agent 归属域</param>
        public SubAgentToolFunction(AIFunction inner, SubAgentCallRecorder recorder, TokenUsageRecorder usage,
            string callerAgentKey, string subAgentKey, string? ownerDomain) : base(inner)
        {
            _recorder = recorder;
            _usage = usage;
            _callerAgentKey = callerAgentKey;
            _subAgentKey = subAgentKey;
            _ownerDomain = ownerDomain;
        }

        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var task = SubAgentCallRecorder.ExtractQuery(arguments);

            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken);

                // 实体函数把用量装在信封里带上来：留痕与出账都用它，模型只看到正文
                if (result is SubAgentRunOutcome outcome)
                {
                    await _recorder.WriteAsync(_callerAgentKey, _subAgentKey, _ownerDomain, task, outcome.Text,
                        EmSubAgentCallStatus.Succeeded, Elapsed(startedAt), null,
                        (int?)outcome.Usage?.InputTokenCount, (int?)outcome.Usage?.OutputTokenCount);

                    await _usage.RecordAsync(_subAgentKey, outcome.Usage, cancellationToken);

                    return outcome.Text;
                }

                await _recorder.WriteAsync(_callerAgentKey, _subAgentKey, _ownerDomain, task, result?.ToString(),
                    EmSubAgentCallStatus.Succeeded, Elapsed(startedAt), null);

                return result;
            }
            catch (OperationCanceledException)
            {
                // 超时与主动取消都从这里出去：留痕记超时，异常照旧往上抛（别把整轮对话吞掉）
                await _recorder.WriteAsync(_callerAgentKey, _subAgentKey, _ownerDomain, task, null,
                    EmSubAgentCallStatus.TimedOut, Elapsed(startedAt), "子 Agent 调用被取消或超时");
                throw;
            }
            catch (Exception ex)
            {
                await _recorder.WriteAsync(_callerAgentKey, _subAgentKey, _ownerDomain, task, null,
                    EmSubAgentCallStatus.Failed, Elapsed(startedAt), ex.Message);
                throw;
            }
        }

        /// <summary>外壳自己的掐表</summary>
        private static int Elapsed(long startedAt) => (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }
}
