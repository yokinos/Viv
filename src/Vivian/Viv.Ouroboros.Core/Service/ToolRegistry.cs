using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    /// 例外是"有需审批绑定"的 Agent：它只缓存 <see cref="ApprovalSensitiveCacheTime"/>，理由见那个常量的注释。
    ///
    /// 本阶段只实现两种工具类型：内置（<see cref="EmToolTransport.Builtin"/>）与 HTTP（<see cref="EmToolTransport.Http"/>）。
    /// MCP（<see cref="EmToolTransport.Mcp"/>）以及将来可能出现的其它类型一律跳过并记 Warning —— **留待后续**。
    ///
    /// MCP 只从"绑定整个服务"这条入口进来（<see cref="EmCapabilityType.McpServer"/>，见
    /// <see cref="BuildMcpServerAsync"/>）：连接、列工具、按 AllowedTools 过滤、逐个包成工具。
    /// 单条 <c>OtTool</c> 写 <see cref="EmToolTransport.Mcp"/> 的那条入口**没有实现**：
    /// OtTool 上没有任何一列能唯一指认"这个工具属于哪个 MCP 服务"，硬拿 Endpoint 当服务名
    /// 就成了一条只存在于代码里的隐式约定（详见 <see cref="SkipUnsupported"/>）。
    ///
    /// 需要人工审批的工具（绑定的 RequiresApproval 覆盖工具自身的，取非空的那个）会包一层
    /// <see cref="ApprovalRequiredAIFunction"/>：模型调用它会产出 <c>ToolApprovalRequestContent</c>，
    /// 从而接上 AgentChatService 里那条既有的"落 OtApproval → 前端批准 → 续跑"链路。
    ///
    /// 缓存键：默认与主体无关。只有绑定既需要审批、又配了主体白名单时，工具列表才随主体不同
    /// （白名单不通过的不套审批壳，见 <see cref="BuildTool"/>），那种 Agent 改按"AgentKey + subjectId"分键，
    /// 并由 <see cref="IsSubjectScoped"/> 告知 AgentFactory 一起分键。
    /// </summary>
    public class ToolRegistry : IToolRegistry, IDependency
    {

        /// <summary>
        /// 有"需审批"绑定的 Agent，其装配结果只缓存 3 秒。套不套审批壳是**装配期**定的
        /// （MAF 只看工具链里有没有 ApprovalRequiredAIFunction，不会先调用工具），而它取决于库里的
        /// AllowedSubjectIds —— 不点 refresh 时只能靠 TTL 收敛。3 秒留出对 5 秒验收窗口的余量，
        /// 代价只是这类 Agent × 主体每 3 秒重查一次绑定表（条目数有界、SQL 很轻）。
        ///
        /// 不能改成"调用时现查白名单再决定套不套壳"：MAF 的判定发生在装配结果上，装配期定不下来
        /// 就只能给所有主体都套壳，白名单外的主体会被人白问一次审批（批准后才知道无权限）。
        /// </summary>
        private static readonly TimeSpan ApprovalSensitiveCacheTime = TimeSpan.FromSeconds(3);

        /// <summary>主体分键的分隔符：只有"需审批 + 配了白名单"的 Agent 才会写出带主体的键</summary>
        private const char SubjectSeparator = '#';

        /// <summary>
        /// MCP 工具名的分隔符：模型看到的是 <c>{服务前缀}__{服务侧工具名}</c>。
        /// 选双下划线是因为 MCP 的工具名里不会出现它（协议允许的字符集里没有连着两个下划线的惯例），
        /// 所以与内置/HTTP 工具的 ToolKey 不会撞车。
        /// </summary>
        private const string McpNameSeparator = "__";

        /// <summary>
        /// 已经缓存过的 key。<see cref="IMemoryCacheService"/> 没有"按前缀清"的能力，
        /// InvalidateAll 只能靠自己记账（条目数 = Agent 数，有界）。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> CachedKeys = new(StringComparer.Ordinal);

        /// <summary>
        /// 已确认"工具列表随主体变"的 AgentKey。只有"需审批 + 配了主体白名单"的绑定会这样，
        /// 而 AgentFactory 把工具嵌在 Agent 里缓存，所以要能读出去跟着一起按主体分键。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> SubjectScopedAgents = new(StringComparer.Ordinal);

        /// <summary>
        /// 已确认"有需审批绑定"的 AgentKey。只增不减（清缓存时才重置）：多算一个只是缓存短一点，
        /// 少算一个却会让白名单改动等到 60 秒 TTL 才生效。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> ApprovalSensitiveAgents = new(StringComparer.Ordinal);

        /// <summary>本进程已用上的配置版本号；与闸门给的版本对不上就说明别处改过配置（见 ConfigVersionGate）</summary>
        private static long _seenGeneration;

        private readonly IAgentStore _store;
        private readonly IEnumerable<IBuiltinToolProvider> _builtinProviders;
        private readonly IMemoryCacheService _cache;
        private readonly ILoggerContract _logger;
        private readonly ToolCallRecorder _recorder;
        private readonly HttpToolExecutor _http;
        private readonly McpClientPool _mcp;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfigVersionGate _version;
        private readonly IOuroborosConfig _config;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="store">库访问（读工具定义、写调用留痕）</param>
        /// <param name="builtinProviders">内置工具提供者（业务侧登记；框架一个都不内置）</param>
        /// <param name="scopeFactory">作用域工厂（留痕与主体白名单校验都要现开作用域，见 ToolCallRecorder）</param>
        /// <param name="cache">内存缓存</param>
        /// <param name="version">配置版本闸门（跨实例失效）</param>
        /// <param name="config">配置读取（工具结果截断上限）</param>
        /// <param name="mcp">MCP 连接池（单例；MCP 工具调用与发现都走它）</param>
        /// <param name="logger">日志</param>
        public ToolRegistry(
            IAgentStore store,
            IEnumerable<IBuiltinToolProvider> builtinProviders,
            IServiceScopeFactory scopeFactory,
            IMemoryCacheService cache,
            IConfigVersionGate version,
            IOuroborosConfig config,
            McpClientPool mcp,
            ILoggerContract logger)
        {
            _store = store;
            _builtinProviders = builtinProviders;
            _cache = cache;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _version = version;
            _config = config;
            _mcp = mcp;
            _recorder = new ToolCallRecorder(scopeFactory, logger);
            _http = new HttpToolExecutor(logger, scopeFactory);
        }

        /// <inheritdoc />
        public async Task<IList<AITool>> GetToolsAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return new List<AITool>();

            RefreshIfConfigChanged();

            var plainKey = BuildCacheKey(agentKey);

            // 命中"与主体无关"的那份就说明这个 Agent 的工具列表不随主体变
            // —— 随主体变的那份从不往这个键上写，所以命中即正确
            if (_cache.TryGet<IList<AITool>>(plainKey, out var plain) && plain is not null) return plain;

            // 走到这里只有两种可能：冷启动，或这个 Agent 有"需审批 + 白名单"的绑定 → 改按主体取
            var subjectId = ResolveSubjectId();
            var subjectKey = BuildSubjectCacheKey(plainKey, subjectId);
            if (_cache.TryGet<IList<AITool>>(subjectKey, out var own) && own is not null) return own;

            var (tools, subjectScoped, approvalSensitive) = await BuildAsync(agentKey, subjectId);
            if (subjectScoped) SubjectScopedAgents[agentKey] = 0;
            if (approvalSensitive) ApprovalSensitiveAgents[agentKey] = 0;

            var storeKey = subjectScoped ? subjectKey : plainKey;
            // 一律用短 TTL：配置可能被「裸改库」改动（不走管理 API 就不会 Publish 版本戳），
            // 若这里用 60 秒，装配时「还不需要审批」的那份会以此为窗口继续免审批执行（代码评审 Bug 2）。
            // 这几张表很小，几秒重建一次换来「改库即生效」是划算的。
            _cache.Set(storeKey, tools, ApprovalSensitiveCacheTime);
            CachedKeys[storeKey] = 0;

            return tools;
        }

        /// <inheritdoc />
        public void Invalidate(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return;

            var prefix = BuildCacheKey(agentKey);
            foreach (var key in CachedKeys.Keys)
            {
                if (!IsKeyOf(key, prefix)) continue;

                _cache.Remove(key);
                CachedKeys.TryRemove(key, out _);
            }

            SubjectScopedAgents.TryRemove(agentKey, out _);
            ApprovalSensitiveAgents.TryRemove(agentKey, out _);
        }

        /// <inheritdoc />
        public void InvalidateAll()
        {
            foreach (var key in CachedKeys.Keys) _cache.Remove(key);
            CachedKeys.Clear();
            SubjectScopedAgents.Clear();
            ApprovalSensitiveAgents.Clear();

            // 配置全量失效（改 MCP 服务、停用、删除都走这条）时顺手把 MCP 连接也放掉：
            // 连接本身按"传输+地址+请求头"指纹复用，改配置不改指纹也不会认错连接；这里丢掉是为了
            // 不让停用/删除后的服务还占着一条连接或一个 stdio 子进程。条目数 = MCP 服务数，有界。
            _mcp.InvalidateAll();
        }

        /// <inheritdoc />
        public bool IsSubjectScoped(string agentKey)
            => !string.IsNullOrWhiteSpace(agentKey) && SubjectScopedAgents.ContainsKey(agentKey);

        /// <inheritdoc />
        public bool IsApprovalSensitive(string agentKey)
            => !string.IsNullOrWhiteSpace(agentKey) && ApprovalSensitiveAgents.ContainsKey(agentKey);

        private static string BuildCacheKey(string agentKey) => $"ouroboros:tools:{agentKey}";

        private static string BuildSubjectCacheKey(string plainKey, long subjectId)
            => $"{plainKey}{SubjectSeparator}{subjectId}";

        /// <summary>键是不是这个 Agent 的（本体键或它的主体分键）</summary>
        private static bool IsKeyOf(string key, string prefix)
            => key.Equals(prefix, StringComparison.Ordinal)
               || (key.Length > prefix.Length && key[prefix.Length] == SubjectSeparator
                   && key.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>别的实例 refresh 过就清掉本进程的工具缓存（节流在闸门里，这里不额外记账）</summary>
        private void RefreshIfConfigChanged()
        {
            var generation = _version.EnsureFresh();
            if (generation == Interlocked.Read(ref _seenGeneration)) return;

            InvalidateAll();
            Interlocked.Exchange(ref _seenGeneration, generation);
            _logger.Info("配置版本已更新（第 {0} 版），工具缓存已清", generation);
        }

        /// <summary>取当前请求的主体 Id：IVivContext 是 Scoped，只能现开作用域解析</summary>
        private long ResolveSubjectId()
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IVivContext>()?.SubjectId ?? 0;
        }

        /// <summary>
        /// 装配工具列表。<paramref name="subjectId"/> 只对"需审批 + 配了主体白名单"的绑定起作用
        /// —— 那种绑定要靠它决定要不要套审批壳（见 <see cref="BuildTool"/>）。
        /// 后两个返回值分别指出"列表随主体变"与"有需审批绑定"，供上层决定缓存键与缓存时长。
        /// </summary>
        private async Task<(IList<AITool> Tools, bool SubjectScoped, bool ApprovalSensitive)> BuildAsync(
            string agentKey, long subjectId)
        {
            var definitions = await _store.ListEnabledToolsAsync(agentKey);
            var tools = new List<AITool>(definitions.Count);
            var subjectScoped = false;
            var approvalSensitive = false;

            foreach (var definition in definitions)
            {
                if (NeedsSubject(definition)) subjectScoped = true;
                if (NeedsApproval(definition)) approvalSensitive = true;

                var tool = BuildTool(agentKey, definition, subjectId);
                if (tool is not null) tools.Add(tool);
            }

            // MCP 是"一个服务出一批工具"，装配期要真连一次；重名检测要看得见上面那些工具的名字，
            // 所以放在后面。用过的名字记账（MCP 工具名 = 前缀 + 服务侧工具名，与其他工具同处一个命名空间）。
            var usedNames = new HashSet<string>(tools.Select(x => x.Name), StringComparer.Ordinal);
            foreach (var mcp in await _store.ListEnabledMcpServersAsync(agentKey))
            {
                var built = await BuildMcpServerAsync(agentKey, mcp, subjectId, usedNames);
                tools.AddRange(built.Tools);
                if (built.SubjectScoped) subjectScoped = true;
                if (built.ApprovalSensitive) approvalSensitive = true;
            }

            return (tools, subjectScoped, approvalSensitive);
        }

        /// <summary>这条绑定要不要套审批壳：绑定上的 RequiresApproval 覆盖工具自身的，取非空的那个</summary>
        private static bool NeedsApproval(AgentToolDefinition definition)
            => definition.Binding.RequiresApproval ?? definition.Tool.RequiresApproval;

        /// <summary>这条绑定会不会让同一份工具列表对不同主体不一样：需审批 + 配了非空白名单</summary>
        private static bool NeedsSubject(AgentToolDefinition definition)
            => NeedsApproval(definition) && !string.IsNullOrWhiteSpace(definition.Binding.AllowedSubjectIds);

        private AITool? BuildTool(string agentKey, AgentToolDefinition definition, long subjectId)
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
            if (!(definition.Binding.RequiresApproval ?? definition.Tool.RequiresApproval)) return function;

            // MAF 判定"这个工具要不要审批"只看工具链里有没有 ApprovalRequiredAIFunction，**不会先调用工具**，
            // 所以把白名单放在外壳里已经太晚：人先被问一次，批准之后才被告知无权限。
            // 放行与否只能在装配期定 —— 不通过的不套审批壳，模型照常调到工具，由外壳当场回"无权限"。
            if (SubjectAllowList.IsAllowed(definition.Binding.AllowedSubjectIds, subjectId, definition.Tool.ToolKey, _logger))
                return new ApprovalRequiredAIFunction(function);

            _logger.Info("主体不在白名单内，该能力不套审批壳（模型会直接收到无权限）：{0} → {1}",
                agentKey, definition.Tool.ToolKey);
            return function;
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

        /// <summary>
        /// 一条 MCP 服务绑定 → 一批工具。
        ///
        /// 命名：<c>{前缀}__{服务侧工具名}</c>，前缀取 <c>OtCapabilityBinding.ExposedName</c>（留空则取服务名）。
        /// 双下划线是刻意的：MCP 自己的工具名里不会出现它，所以与内置/HTTP 工具的 ToolKey 不会撞车；
        /// 同时前缀可被 ExposedName 覆盖，正是那一列"避免重名"的用途（一个服务出 N 个工具，覆盖的只能是前缀）。
        /// 重名（本次装配里已经有同名工具）就跳过这一个并记 Warning：同名工具交给模型只会派发歧义。
        ///
        /// 暴露范围：<c>AllowedTools</c> 非空时只放行名单里的服务侧工具名（拿原名比，不是加前缀后的名字）。
        /// 审批与白名单口径与普通工具完全一致：<c>RequiresApproval</c> 见 <see cref="McpServerPolicy.RequiresApproval"/>，
        /// 主体白名单装配期只决定"套不套审批壳"，真正的拒绝在 <see cref="RegisteredToolFunction"/> 里按请求现算。
        ///
        /// 连不上/列不出工具只跳过这条绑定并记 Warning，绝不让一条坏配置把整个 Agent 打死 ——
        /// 此时模型看不到该服务的工具（而不是收到一堆报错工具）。
        /// </summary>
        private async Task<(IReadOnlyList<AITool> Tools, bool SubjectScoped, bool ApprovalSensitive)> BuildMcpServerAsync(
            string agentKey, AgentMcpServerDefinition definition, long subjectId, HashSet<string> usedNames)
        {
            var binding = definition.Binding;
            var server = definition.Server;
            var tools = new List<AITool>();

            IReadOnlyList<McpToolDescriptor> discovered;
            try
            {
                discovered = await _mcp.ListToolsAsync(server, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warning("MCP 服务连接或列工具失败，本次装配跳过该绑定：{0} → {1}，{2}", agentKey, server.ServerName, ex.Message);
                return (tools, false, false);
            }

            if (!string.IsNullOrWhiteSpace(binding.ExposedDescription))
                _logger.Info("MCP 服务绑定的 ExposedDescription 不适用（一个服务有多个工具，无法用一句描述覆盖），已忽略：{0} → {1}",
                    agentKey, server.ServerName);

            var allowed = McpServerPolicy.ParseNames(server.AllowedTools, server.ServerName, nameof(server.AllowedTools), _logger);
            var always = McpServerPolicy.ParseNames(server.AlwaysRequireToolNames, server.ServerName, nameof(server.AlwaysRequireToolNames), _logger);
            var never = McpServerPolicy.ParseNames(server.NeverRequireToolNames, server.ServerName, nameof(server.NeverRequireToolNames), _logger);

            var prefix = string.IsNullOrWhiteSpace(binding.ExposedName) ? server.ServerName : binding.ExposedName;
            var subjectScoped = false;
            var approvalSensitive = false;

            foreach (var descriptor in discovered.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (allowed.Count > 0 && !allowed.Contains(descriptor.Name))
                {
                    _logger.Info("MCP 工具不在 AllowedTools 名单内，未暴露：{0} → {1}", server.ServerName, descriptor.Name);
                    continue;
                }

                var name = $"{prefix}{McpNameSeparator}{descriptor.Name}";
                if (!usedNames.Add(name))
                {
                    _logger.Warning("MCP 工具名与已有工具重名，已跳过：{0}（来源 {1}）", name, server.ServerName);
                    continue;
                }

                // 服务没给 schema 就交 null，让外壳退回框架推断的那份（别把 Undefined 顶给模型）
                JsonElement? schema = descriptor.Schema.ValueKind == JsonValueKind.Undefined ? null : descriptor.Schema;
                var description = string.IsNullOrWhiteSpace(descriptor.Description)
                    ? $"MCP 服务 {server.ServerName} 提供的工具：{descriptor.Name}"
                    : descriptor.Description;

                var requiresApproval = McpServerPolicy.RequiresApproval(server.ApprovalMode, binding.RequiresApproval,
                    descriptor.Name, always, never, server.ServerName, _logger);

                // 与 BuildHttp 同一理由：只把单例的池传进闭包，不碰 this
                var function = new McpToolFunction(_mcp, server.ServerName, descriptor.Name, name, description, schema);

                var registered = new RegisteredToolFunction(
                    function,
                    agentKey,
                    name,
                    requiresApproval,
                    schema,
                    binding.AllowedSubjectIds,
                    _recorder,
                    _scopeFactory,
                    _config,
                    _logger);

                if (!requiresApproval)
                {
                    tools.Add(registered);
                    continue;
                }

                approvalSensitive = true;
                if (!string.IsNullOrWhiteSpace(binding.AllowedSubjectIds)) subjectScoped = true;

                // 与工具/子 Agent 同一条口径：白名单不通过就不套审批壳，否则人先被白问一次
                if (SubjectAllowList.IsAllowed(binding.AllowedSubjectIds, subjectId, name, _logger))
                {
                    tools.Add(new ApprovalRequiredAIFunction(registered));
                }
                else
                {
                    _logger.Info("主体不在白名单内，该 MCP 工具不套审批壳（模型会直接收到无权限）：{0} → {1}",
                        agentKey, name);
                    tools.Add(registered);
                }
            }

            // 有工具要审批但没有工具套上壳（白名单全不通过）时不算"敏感"，与 NeedsSubject 的口径保持一致即可
            return (tools, subjectScoped, approvalSensitive);
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
                _config,
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

        /// <summary>
        /// 单条工具写 MCP 传输时跳过。**这是刻意的，不是没做完**：
        /// <c>OtTool</c> 上没有一列能唯一指认"这个工具属于哪个 MCP 服务"（Endpoint 的注释是"调用地址"，
        /// ToolKey 是模型可见名、不保证等于服务侧工具名），硬把 Endpoint 当服务名就成了一条
        /// 只存在于代码里的隐式约定，还会绕开 OtMcpServer 上的 Headers / ApprovalMode / AllowedTools。
        /// 要单点暴露 MCP 工具，请改成"绑定整个服务"再配 <c>AllowedTools</c>。
        /// </summary>
        private AIFunction? SkipUnsupported(OtTool tool)
        {
            _logger.Warning("单条工具写 MCP 传输无法唯一确定所属 MCP 服务，已跳过（请改用「绑定整个 MCP 服务 + AllowedTools」）：{0}（Transport={1}）",
                tool.ToolKey, tool.Transport.ToString());
            return null;
        }
    }
}
