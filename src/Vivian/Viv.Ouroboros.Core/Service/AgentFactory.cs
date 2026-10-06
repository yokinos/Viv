using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Viv.Contracts.Interface;
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
    /// 工具：能力绑定已经读出来了，但工具的"实现"要等 P2 的工具注册表接入，
    /// 这里先把绑定数记日志 —— 免得看起来像"绑了却没用上"。
    /// </summary>
    public class AgentFactory : IAgentFactory, IDependency
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

        private readonly IAgentRepository _repository;
        private readonly IModelProfileProvider _profiles;
        private readonly IMemoryCacheService _cache;
        private readonly ILoggerContract _logger;

        public AgentFactory(
            IAgentRepository repository,
            IModelProfileProvider profiles,
            IMemoryCacheService cache,
            ILoggerContract logger)
        {
            _repository = repository;
            _profiles = profiles;
            _cache = cache;
            _logger = logger;
        }

        public async Task<AIAgent?> GetAgentAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return null;

            var cacheKey = $"ouroboros:agent:{agentKey}";

            // 只缓存装配成功的结果：null（未定义/未启用/没提示词/档位不可用）不进缓存，
            // 否则"刚在库里配好"也要等 TTL 到期才生效
            if (_cache.TryGet<AIAgent>(cacheKey, out var cached) && cached is not null) return cached;

            var agent = await BuildAsync(agentKey, CancellationToken.None);
            if (agent is not null) _cache.Set(cacheKey, agent, CacheTime);

            return agent;
        }

        public void Invalidate(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return;
            _cache.Remove($"ouroboros:agent:{agentKey}");
        }

        private async ValueTask<AIAgent?> BuildAsync(string agentKey, CancellationToken token)
        {
            var definition = await _repository.GetByKeyAsync(agentKey);
            if (definition is null)
            {
                _logger.Warning("Agent 未定义：{0}", agentKey);
                return null;
            }

            if (!definition.IsEnabled)
            {
                _logger.Warning("Agent 未启用：{0}", agentKey);
                return null;
            }

            var instructions = await _repository.GetPromptContentAsync(agentKey, definition.ActivePromptVersion);
            if (string.IsNullOrWhiteSpace(instructions))
            {
                _logger.Error("Agent 没有可用提示词（ActivePromptVersion={0}）：{1}", definition.ActivePromptVersion, agentKey);
                return null;
            }

            var client = await _profiles.GetChatClientAsync(definition.ModelProfile);
            if (client is null)
            {
                _logger.Error("Agent 的模型档位不可用：{0} → {1}", agentKey, definition.ModelProfile);
                return null;
            }

            var capabilities = await _repository.GetCapabilitiesAsync(agentKey);

            var agent = client.AsAIAgent(new ChatClientAgentOptions
            {
                // Id 必须稳定：它是路由、会话与 checkpoint 复水的身份
                Id = definition.AgentKey,
                Name = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.AgentKey : definition.DisplayName,
                Description = definition.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions
                }
            });

            _logger.Info("Agent 已装配：{0}（档位 {1}，能力绑定 {2} 条）", agentKey, definition.ModelProfile, capabilities.Count);
            return agent;
        }
    }
}
