using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 一个 MCP 工具在模型侧的样子：名字、描述、入参 schema 全来自服务，
    /// 调用则转交 <see cref="McpClientPool"/>（按服务名 + 服务侧工具名现取连接）。
    ///
    /// 刻意不持 SDK 的 <c>McpClientTool</c>：那样等于把"装配那一刻的连接"钉进会被缓存 60 秒的闭包，
    /// 连接一断或配置一改就成了僵尸。这里只持**单例**的池与两个字符串，没有 captive dependency。
    ///
    /// 失败不做二次包装：池抛的 <see cref="IService.ToolExecutionException"/> 会让外层
    /// <see cref="RegisteredToolFunction"/> 落一条失败的 <c>OtToolCall</c> 并把原因当工具结果交回模型。
    /// </summary>
    public sealed class McpToolFunction : AIFunction
    {
        private readonly McpClientPool _pool;
        private readonly string _serverName;
        private readonly string _remoteName;
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement? _schema;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pool">MCP 连接池（单例）</param>
        /// <param name="serverName">服务名（取连接用）</param>
        /// <param name="remoteName">服务侧的原始工具名（调用时用它，不是加前缀的那个）</param>
        /// <param name="name">模型可见的工具名（已加服务前缀）</param>
        /// <param name="description">给模型看的描述（服务侧的 description）</param>
        /// <param name="schema">服务侧给的入参 schema；服务没给就退回框架的默认 schema</param>
        public McpToolFunction(McpClientPool pool, string serverName, string remoteName, string name, string description,
            JsonElement? schema)
        {
            _pool = pool;
            _serverName = serverName;
            _remoteName = remoteName;
            _name = name;
            _description = description;
            _schema = schema;
        }

        /// <inheritdoc />
        public override string Name => _name;

        /// <inheritdoc />
        public override string Description => _description;

        /// <summary>服务侧给的 schema 原样顶上去：模型选不选得对工具、参数给得对不对全看它</summary>
        public override JsonElement JsonSchema
            => _schema is { ValueKind: not JsonValueKind.Undefined } schema ? schema : base.JsonSchema;

        /// <inheritdoc />
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var payload = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
            foreach (var pair in arguments) payload[pair.Key] = pair.Value;

            return await _pool.InvokeAsync(_serverName, _remoteName, payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
