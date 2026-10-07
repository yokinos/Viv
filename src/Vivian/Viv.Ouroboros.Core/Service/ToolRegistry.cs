using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Ouroboros.Core.IService;

using Viv.Ouroboros.Core.Entity.Model.Agent;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 工具注册表实现。
    ///
    /// 缓存：按 AgentKey 缓存装配好的 <see cref="AITool"/> 列表 60 秒（与 AgentFactory、档位同一节奏），
    /// 因为外层 AgentFactory 也只缓存 60 秒 —— 两边一起过期才不会出现"工具换了、Agent 还是旧的"。
    ///
    /// 本阶段只实现两种工具类型：内置（<see cref="EmToolTransport.Builtin"/>）与 HTTP（<see cref="EmToolTransport.Http"/>）。
    /// MCP（<see cref="EmToolTransport.Mcp"/>）以及将来可能出现的其它类型一律跳过并记 Warning —— **留待后续**。
    ///
    /// 需要人工审批的工具（绑定的 RequiresApproval 覆盖工具自身的，取非空的那个）会包一层
    /// <see cref="ApprovalRequiredAIFunction"/>：模型调用它会产出 <c>ToolApprovalRequestContent</c>，
    /// 从而接上 AgentChatService 里那条既有的"落 OtApproval → 前端批准 → 续跑"链路。
    /// </summary>
    public class ToolRegistry : IToolRegistry, IDependency
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 已经缓存过的 key。<see cref="IMemoryCacheService"/> 没有"按前缀清"的能力，
        /// InvalidateAll 只能靠自己记账（条目数 = Agent 数，有界）。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> CachedKeys = new(StringComparer.Ordinal);

        private readonly IAgentStore _store;
        private readonly IEnumerable<IBuiltinToolProvider> _builtinProviders;
        private readonly IMemoryCacheService _cache;
        private readonly ILoggerContract _logger;
        private readonly ToolCallRecorder _recorder;
        private readonly HttpToolExecutor _http;
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="store">库访问（读工具定义、写调用留痕）</param>
        /// <param name="builtinProviders">内置工具提供者（业务侧登记；框架一个都不内置）</param>
        /// <param name="scopeFactory">作用域工厂（留痕与主体白名单校验都要现开作用域，见 ToolCallRecorder）</param>
        /// <param name="cache">内存缓存</param>
        /// <param name="logger">日志</param>
        public ToolRegistry(
            IAgentStore store,
            IEnumerable<IBuiltinToolProvider> builtinProviders,
            IServiceScopeFactory scopeFactory,
            IMemoryCacheService cache,
            ILoggerContract logger)
        {
            _store = store;
            _builtinProviders = builtinProviders;
            _cache = cache;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _recorder = new ToolCallRecorder(scopeFactory, logger);
            _http = new HttpToolExecutor(logger, scopeFactory);
        }

        /// <inheritdoc />
        public async Task<IList<AITool>> GetToolsAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return new List<AITool>();

            var cacheKey = BuildCacheKey(agentKey);
            if (_cache.TryGet<IList<AITool>>(cacheKey, out var cached) && cached is not null) return cached;

            var tools = await BuildAsync(agentKey);
            _cache.Set(cacheKey, tools, CacheTime);
            CachedKeys[cacheKey] = 0;

            return tools;
        }

        /// <inheritdoc />
        public void Invalidate(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return;
            _cache.Remove(BuildCacheKey(agentKey));
        }

        /// <inheritdoc />
        public void InvalidateAll()
        {
            foreach (var key in CachedKeys.Keys) _cache.Remove(key);
            CachedKeys.Clear();
        }

        private static string BuildCacheKey(string agentKey) => $"ouroboros:tools:{agentKey}";

        private async Task<IList<AITool>> BuildAsync(string agentKey)
        {
            var definitions = await _store.ListEnabledToolsAsync(agentKey);
            var tools = new List<AITool>(definitions.Count);

            foreach (var definition in definitions)
            {
                var tool = BuildTool(agentKey, definition);
                if (tool is not null) tools.Add(tool);
            }

            return tools;
        }

        private AITool? BuildTool(string agentKey, AgentToolDefinition definition)
        {
            // 绑定上的暴露名/暴露描述是刻意用来消重名的，优先级高于工具自身
            var name = string.IsNullOrWhiteSpace(definition.Binding.ExposedName)
                ? definition.Tool.ToolKey
                : definition.Binding.ExposedName;

            var description = string.IsNullOrWhiteSpace(definition.Binding.ExposedDescription)
                ? definition.Tool.Description ?? string.Empty
                : definition.Binding.ExposedDescription;

            var function = definition.Tool.Transport switch
            {
                EmToolTransport.Builtin => BuildBuiltin(agentKey, definition, name, description),
                EmToolTransport.Http => BuildHttp(agentKey, definition, name, description),
                _ => SkipUnsupported(definition.Tool)
            };

            if (function is null) return null;

            var requiresApproval = definition.Binding.RequiresApproval ?? definition.Tool.RequiresApproval;
            return requiresApproval ? new ApprovalRequiredAIFunction(function) : function;
        }

        /// <summary>
        /// 内置工具：按 ToolKey 查业务侧登记的实现。查不到只跳过这一个并记 Warning，
        /// 不让"某个内置工具还没实现"变成"整个 Agent 装不出来"。
        /// </summary>
        private AIFunction? BuildBuiltin(string agentKey, AgentToolDefinition definition, string name, string description)
        {
            var toolKey = definition.Tool.ToolKey;
            Delegate? implementation = null;

            foreach (var provider in _builtinProviders)
            {
                if (provider.TryGetImplementation(toolKey, out var found) && found is not null)
                {
                    implementation = found;
                    break;
                }
            }

            if (implementation is null)
            {
                _logger.Warning("内置工具没有实现，已跳过：{0}", toolKey);
                return null;
            }

            // 名字与描述必须显式给：不给的话工厂按委托名生成 _Main_g_Xxx_1 这种不可控名字
            return Wrap(agentKey, definition, AIFunctionFactory.Create(implementation, name, description));
        }

        /// <summary>
        /// HTTP 工具：通用执行器，按 Endpoint 与入参发请求。
        /// </summary>
        private AIFunction? BuildHttp(string agentKey, AgentToolDefinition definition, string name, string description)
        {
            var tool = definition.Tool;
            if (string.IsNullOrWhiteSpace(tool.Endpoint))
            {
                _logger.Warning("HTTP 工具没有配置 Endpoint，已跳过：{0}", tool.ToolKey);
                return null;
            }

            // 先取到局部变量：直接写 _http 会编译成 this._http，等于把本注册表（Scoped）连同它持有的
            // Scoped 仓储一起钉进闭包 —— 而装配好的 Agent 会被 AgentFactory 缓存 60 秒、跨请求复用。
            // 执行器与工具函数只持日志和作用域工厂，Scoped 的 HTTP 服务由它每次调用现开作用域解析。
            var executor = _http;

            return Wrap(agentKey, definition, new HttpToolFunction(executor, tool, name, description));
        }

        /// <summary>套上统一外壳：ParamsSchema 顶 schema + 主体白名单校验 + 调用留痕</summary>
        private AIFunction Wrap(string agentKey, AgentToolDefinition definition, AIFunction inner)
            => new RegisteredToolFunction(
                inner,
                agentKey,
                definition.Tool.ToolKey,
                definition.Binding.RequiresApproval ?? definition.Tool.RequiresApproval,
                ParseSchema(definition.Tool),
                definition.Binding.AllowedSubjectIds,
                _recorder,
                _scopeFactory,
                _logger);

        /// <summary>OtTool.ParamsSchema 是字符串，转成 JsonElement 给模型看；坏 JSON 退回工厂推断的 schema</summary>
        private JsonElement? ParseSchema(OtTool tool)
        {
            if (string.IsNullOrWhiteSpace(tool.ParamsSchema)) return null;

            try
            {
                using var document = JsonDocument.Parse(tool.ParamsSchema);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.Warning("工具入参 Schema 不是合法 JSON，改按推断：{0}，{1}", tool.ToolKey, ex.Message);
                return null;
            }
        }

        /// <summary>本阶段不实现的传输方式（MCP 等）。留待后续：接 MCP 客户端后在这里换成工具发现结果。</summary>
        private AIFunction? SkipUnsupported(OtTool tool)
        {
            _logger.Warning("工具类型暂不支持，已跳过（MCP / 其它类型留待后续）：{0}（Transport={1}）", tool.ToolKey, tool.Transport.ToString());
            return null;
        }
    }
}
