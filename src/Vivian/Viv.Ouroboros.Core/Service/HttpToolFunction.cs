using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// HTTP 工具的 <see cref="AIFunction"/>：把工具定义与执行器绑到一起，调用时只做两件事 ——
    /// 成功把执行器的 <see cref="HttpToolResult"/>（含服务报的耗时）原样交给外壳，
    /// 失败抛 <see cref="ToolExecutionException"/> 让外壳留痕记失败、把原因当结果交回模型。
    ///
    /// 不用 <c>AIFunctionFactory.Create</c>：它会把非 string 的返回值序列化成 JsonElement，
    /// 信封传不到外壳、模型还会看到 <c>{"text":...,"succeeded":...}</c> 这种内部结构。
    /// </summary>
    public sealed class HttpToolFunction : AIFunction
    {
        /// <summary>没有 ParamsSchema 时给模型的空对象（与工厂原先的推断结果一致）；有则被外壳顶掉</summary>
        private static readonly JsonElement EmptyObjectSchema =
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

        /// <summary>对模型而言结果就是文本（信封在外壳那层就拆了），别让它看见信封的结构</summary>
        private static readonly JsonElement StringReturnSchema =
            JsonDocument.Parse("""{"type":"string"}""").RootElement.Clone();

        private readonly HttpToolExecutor _executor;
        private readonly OtTool _tool;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="executor">HTTP 执行器（只持日志与作用域工厂，可以被缓存住的闭包长期持有）</param>
        /// <param name="tool">工具定义（用其中的 Endpoint）</param>
        /// <param name="name">暴露给模型的名字</param>
        /// <param name="description">暴露给模型的描述</param>
        public HttpToolFunction(HttpToolExecutor executor, OtTool tool, string name, string description)
        {
            _executor = executor;
            _tool = tool;
            Name = name;
            Description = description;
        }

        /// <inheritdoc />
        public override string Name { get; }

        /// <inheritdoc />
        public override string Description { get; }

        /// <inheritdoc />
        public override JsonElement JsonSchema => EmptyObjectSchema;

        /// <inheritdoc />
        public override JsonElement? ReturnJsonSchema => StringReturnSchema;

        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var outcome = await _executor.SendAsync(_tool, arguments, cancellationToken);

            // 非 2xx 不当异常抛给上层：抛这个标记，外壳会留痕 <see cref="EmToolCallStatus.Failed"/> 并把它变成模型可见的结果
            if (!outcome.Succeeded) throw new ToolExecutionException(outcome.Text, outcome.Error, outcome.LatencyMs);

            return outcome;
        }
    }
}
