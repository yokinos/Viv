using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Ouroboros.Core.IRepository;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// Agent 工厂实现。
    ///
    /// 缓存：按 AgentKey 缓存装配好的 <see cref="AIAgent"/> 60 秒（与档位同一节奏），
    /// 避免每次请求都查三张表 + 重建 Agent。配置变更靠 TTL 生效，需要立即生效就调 Invalidate。
    ///
    /// 工具：能力绑定 → 工具定义 → AIFunction 这一段在 <see cref="IToolRegistry"/> 里，
    /// 这里只把结果塞进 ChatOptions.Tools。两者缓存时长一致，不会出现"工具换了、Agent 还是旧的"。
    ///
    /// 子 Agent：<c>CapabilityType = SubAgent</c> 的绑定在这里递归装配成工具
    /// （自己实现的 <see cref="SubAgentRunnerFunction"/>，为了拿到子会话的 token 用量）。
    /// 递归不走 DI（那会形成 AgentFactory 依赖自己的环），而是在本实例内下潜，
    /// 并用一条"正在装配的 AgentKey"链 + 深度上限挡住 A→B→A 这类环。
    ///
    /// 缓存键：默认与主体无关。但工具（或子 Agent 绑定）里只要有"需审批 + 配了主体白名单"的，
    /// 装配结果就随主体不同（见 <see cref="ToolRegistry"/>），那种 Agent 改按"AgentKey + subjectId"分键。
    /// </summary>
    public class AgentFactory : IAgentFactory, IDependency
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

        /// <summary>主体分键的分隔符，与 ToolRegistry 同一手法</summary>
        private const char SubjectSeparator = '#';

        /// <summary>子 Agent 嵌套装配的深度上限，兜住"链没成环但无限长"的配置</summary>
        private const int MaxSubAgentDepth = 4;

        /// <summary>
        /// 已经缓存过的 key。与 <see cref="ToolRegistry"/> 同一手法：
        /// <see cref="IMemoryCacheService"/> 没有"按前缀清"的能力，InvalidateAll 只能自己记账（条目数 = Agent 数，有界）。
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> CachedKeys = new(StringComparer.Ordinal);

        /// <summary>本进程已用上的配置版本号；与闸门给的版本对不上就说明别处改过配置（见 ConfigVersionGate）</summary>
        private static long _seenGeneration;

        private readonly IAgentRepository _repository;
        private readonly IModelProfileProvider _profiles;
        private readonly IToolRegistry _tools;
        private readonly IMemoryCacheService _cache;
        private readonly ILoggerContract _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ToolCallRecorder _recorder;
        private readonly SubAgentCallRecorder _subAgentRecorder;
        private readonly IConfigVersionGate _version;

        public AgentFactory(
            IAgentRepository repository,
            IModelProfileProvider profiles,
            IToolRegistry tools,
            IServiceScopeFactory scopeFactory,
            IMemoryCacheService cache,
            IConfigVersionGate version,
            ILoggerContract logger)
        {
            _repository = repository;
            _profiles = profiles;
            _tools = tools;
            _cache = cache;
            _logger = logger;
            // 留痕器只持日志与作用域工厂（都是 Singleton），可以被缓存住的工具闭包长期持有
            _recorder = new ToolCallRecorder(scopeFactory, logger);
            _subAgentRecorder = new SubAgentCallRecorder(scopeFactory, logger);
            // 主体白名单校验要现开作用域取 IVivContext（Scoped），所以工厂自己也留着工厂
            _scopeFactory = scopeFactory;
            _version = version;
        }

        public async Task<AIAgent?> GetAgentAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return null;

            RefreshIfConfigChanged();

            var plainKey = BuildCacheKey(agentKey);

            // 只缓存装配成功的结果：null（未定义/未启用/没提示词/档位不可用）不进缓存，
            // 否则"刚在库里配好"也要等 TTL 到期才生效
            // 命中"与主体无关"的那份就说明装配结果不随主体变 —— 随主体变的那份从不往这个键上写
            if (_cache.TryGet<AIAgent>(plainKey, out var plain) && plain is not null) return plain;

            // 走到这里只有两种可能：冷启动，或工具/子 Agent 里有"需审批 + 白名单"的绑定 → 改按主体取
            var subjectId = ResolveSubjectId();
            var subjectKey = BuildSubjectCacheKey(plainKey, subjectId);
            if (_cache.TryGet<AIAgent>(subjectKey, out var own) && own is not null) return own;

            IReadOnlySet<string> chain = new HashSet<string>(StringComparer.Ordinal) { agentKey };
            var (agent, subjectScoped) = await BuildAsync(agentKey, subjectId, null, chain, CancellationToken.None);
            if (agent is null) return null;

            var storeKey = subjectScoped ? subjectKey : plainKey;
            _cache.Set(storeKey, agent, CacheTime);
            CachedKeys[storeKey] = 0;

            return agent;
        }

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
        }

        /// <inheritdoc />
        public void InvalidateAll()
        {
            foreach (var key in CachedKeys.Keys) _cache.Remove(key);
            CachedKeys.Clear();
        }

        private static string BuildCacheKey(string agentKey) => $"ouroboros:agent:{agentKey}";

        private static string BuildSubjectCacheKey(string plainKey, long subjectId)
            => $"{plainKey}{SubjectSeparator}{subjectId}";

        /// <summary>键是不是这个 Agent 的（本体键或它的主体分键）</summary>
        private static bool IsKeyOf(string key, string prefix)
            => key.Equals(prefix, StringComparison.Ordinal)
               || (key.Length > prefix.Length && key[prefix.Length] == SubjectSeparator
                   && key.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>别的实例 refresh 过就清掉本进程的装配缓存（节流在闸门里）</summary>
        private void RefreshIfConfigChanged()
        {
            var generation = _version.EnsureFresh();
            if (generation == Interlocked.Read(ref _seenGeneration)) return;

            InvalidateAll();
            Interlocked.Exchange(ref _seenGeneration, generation);
            _logger.Info("配置版本已更新（第 {0} 版），Agent 装配缓存已清", generation);
        }

        /// <summary>取当前请求的主体 Id：IVivContext 是 Scoped，只能现开作用域解析</summary>
        private long ResolveSubjectId()
        {
            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IVivContext>()?.SubjectId ?? 0;
        }

        /// <summary>
        /// 装配一个 Agent。<paramref name="ancestors"/> 是"正在装配的 AgentKey 链"（**含自己**），
        /// 环检测与深度上限都靠它；<paramref name="known"/> 是调用方已经读到的定义，避免子 Agent 重复查库。
        /// 第二个返回值指出这份装配是否随主体不同 —— 决定上层缓存要不要按主体分键。
        /// </summary>
        private async ValueTask<(AIAgent? Agent, bool SubjectScoped)> BuildAsync(string agentKey, long subjectId,
            OtAgent? known, IReadOnlySet<string> ancestors, CancellationToken token)
        {
            var definition = known ?? await _repository.GetByKeyAsync(agentKey);
            if (definition is null)
            {
                _logger.Warning("Agent 未定义：{0}", agentKey);
                return (null, false);
            }

            if (!definition.IsEnabled)
            {
                _logger.Warning("Agent 未启用：{0}", agentKey);
                return (null, false);
            }

            var instructions = await _repository.GetPromptContentAsync(agentKey, definition.ActivePromptVersion);
            if (string.IsNullOrWhiteSpace(instructions))
            {
                _logger.Error("Agent 没有可用提示词（ActivePromptVersion={0}）：{1}", definition.ActivePromptVersion, agentKey);
                return (null, false);
            }

            var client = await _profiles.GetChatClientAsync(definition.ModelProfile);
            if (client is null)
            {
                _logger.Error("Agent 的模型档位不可用：{0} → {1}", agentKey, definition.ModelProfile);
                return (null, false);
            }

            var capabilities = await _repository.GetCapabilitiesAsync(agentKey);
            var tools = new List<AITool>(await _tools.GetToolsAsync(agentKey));

            // 工具列表随主体变（有"需审批 + 白名单"的绑定）时，这份 Agent 也不能跨主体复用
            var subjectScoped = _tools.IsSubjectScoped(agentKey);
            var subAgentCount = 0;

            // 子 Agent 与工具一样是"能力"：绑定才有，没绑定模型看不到也就调不到
            foreach (var binding in capabilities.Where(x => x.CapabilityType == EmCapabilityType.SubAgent))
            {
                var (tool, subScoped) = await BuildSubAgentToolAsync(agentKey, binding, subjectId, ancestors, token);
                if (tool is null) continue;

                tools.Add(tool);
                if (subScoped) subjectScoped = true;
                subAgentCount++;
            }

            var agent = client.AsAIAgent(new ChatClientAgentOptions
            {
                // Id 必须稳定：它是路由、会话与 checkpoint 复水的身份
                Id = definition.AgentKey,
                Name = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.AgentKey : definition.DisplayName,
                Description = definition.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    // 没绑工具就保持 null：空列表会让部分 provider 白发一个空的 tools 字段
                    Tools = tools.Count == 0 ? null : new List<AITool>(tools)
                }
            });

            _logger.Info("Agent 已装配：{0}（档位 {1}，能力绑定 {2} 条，可用工具 {3} 个，其中子 Agent {4} 个）",
                agentKey, definition.ModelProfile, capabilities.Count, tools.Count, subAgentCount);
            return (agent, subjectScoped);
        }

        /// <summary>
        /// 把一条 SubAgent 能力绑定装配成工具。三种情况跳过并记 Warning，不让一条坏配置把整个 Agent 打死：
        /// 能力键为空 / 成环或超过深度上限 / 子 Agent 未定义、未启用或没提示词（递归里已经记过）。
        /// </summary>
        private async ValueTask<(AITool? Tool, bool SubjectScoped)> BuildSubAgentToolAsync(string callerKey,
            OtCapabilityBinding binding, long subjectId, IReadOnlySet<string> ancestors, CancellationToken token)
        {
            var subKey = binding.CapabilityKey;
            if (string.IsNullOrWhiteSpace(subKey))
            {
                _logger.Warning("子 Agent 能力绑定的 CapabilityKey 为空，已跳过：{0}", callerKey);
                return (null, false);
            }

            // 环检测：待装配的子 Agent 已经在"正在装配"的链上（A→B→A），再下去就是无限递归
            if (ancestors.Contains(subKey))
            {
                _logger.Warning("子 Agent 绑定成环，已跳过：{0} → {1}（装配链 {2}）",
                    callerKey, subKey, string.Join(" → ", ancestors));
                return (null, false);
            }

            if (ancestors.Count >= MaxSubAgentDepth)
            {
                _logger.Warning("子 Agent 装配深度超过上限 {0}，已跳过：{1} → {2}（装配链 {3}）",
                    MaxSubAgentDepth, callerKey, subKey, string.Join(" → ", ancestors));
                return (null, false);
            }

            var definition = await _repository.GetByKeyAsync(subKey);
            if (definition is null || !definition.IsEnabled)
            {
                _logger.Warning("能力绑定指向的子 Agent 不存在或未启用，已跳过：{0} → {1}", callerKey, subKey);
                return (null, false);
            }

            // 链上加上"子 Agent 自己"再下潜：它的档位、提示词、它自己的工具全部走同一套装配逻辑
            var next = new HashSet<string>(ancestors, StringComparer.Ordinal) { subKey };
            var (child, childScoped) = await BuildAsync(subKey, subjectId, definition, next, token);
            if (child is null)
            {
                _logger.Warning("子 Agent 装配失败，已跳过：{0} → {1}", callerKey, subKey);
                return (null, false);
            }

            // 暴露名/暴露描述刻意用来消重名，优先级高于子 Agent 自身
            var name = string.IsNullOrWhiteSpace(binding.ExposedName) ? subKey : binding.ExposedName;
            var description = string.IsNullOrWhiteSpace(binding.ExposedDescription)
                ? definition.Description ?? string.Empty
                : binding.ExposedDescription;

            var function = new SubAgentRunnerFunction(child, name, description);

            // 由内到外：子 Agent 留痕（OtSubAgentCall）→ 工具留痕 + 主体白名单外壳（OtToolCall）→ 需要时再套审批
            var recorded = new SubAgentToolFunction(function, _subAgentRecorder, callerKey, subKey, definition.OwnerDomain);
            var registered = new RegisteredToolFunction(recorded, callerKey, name,
                binding.RequiresApproval ?? false, null, binding.AllowedSubjectIds, _recorder, _scopeFactory, _logger);

            var requiresApproval = binding.RequiresApproval == true;
            var subjectScoped = requiresApproval && !string.IsNullOrWhiteSpace(binding.AllowedSubjectIds)
                                || childScoped;

            // 与工具那条同一个道理：MAF 只看工具链里有没有审批壳，不看调用结果，
            // 所以白名单不通过时不能套壳，否则人会被白问一次，批准后才知道无权限。
            if (!requiresApproval) return (registered, subjectScoped);

            if (SubjectAllowList.IsAllowed(binding.AllowedSubjectIds, subjectId, subKey, _logger))
                return (new ApprovalRequiredAIFunction(registered), subjectScoped);

            _logger.Info("主体不在白名单内，该子 Agent 能力不套审批壳（模型会直接收到无权限）：{0} → {1}",
                callerKey, subKey);
            return (registered, subjectScoped);
        }
    }
}
