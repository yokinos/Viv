using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 自检实现：档位解析、Agent 装配、真实对话、清缓存
    /// </summary>
    public class AgentDiagnosticsService : IAgentDiagnosticsService, IDependency
    {
        private readonly IModelProfileProvider _profiles;
        private readonly IAgentFactory _agents;

        public AgentDiagnosticsService(IModelProfileProvider profiles, IAgentFactory agents)
        {
            _profiles = profiles;
            _agents = agents;
        }

        /// <summary>
        /// 看某个档位解析成什么（不回密钥，只回有没有取到）
        /// </summary>
        public async Task<VivApiResult> GetProfileAsync(string profileKey)
        {
            var profile = await _profiles.GetProfileAsync(profileKey);
            if (profile is null) return VivApiResult.Failed($"档位不可用：{profileKey}");

            return VivApiResult.Success(new ModelProfileOutput
            {
                ProfileKey = profile.ProfileKey,
                Model = profile.Model,
                ApiUrl = profile.ApiUrl,
                HasKey = !string.IsNullOrEmpty(profile.ApiKey),
                Temperature = profile.Temperature,
                MaxOutputTokens = profile.MaxOutputTokens,
                Priority = profile.Priority
            });
        }

        /// <summary>
        /// 看某个 Agent 能否装配出来
        /// </summary>
        public async Task<VivApiResult> GetAgentAsync(string agentKey)
        {
            var agent = await _agents.GetAgentAsync(agentKey);
            if (agent is null) return VivApiResult.Failed($"Agent 未就绪：{agentKey}");

            return VivApiResult.Success(new AgentInfoOutput
            {
                Id = agent.Id,
                Name = agent.Name,
                Description = agent.Description
            });
        }

        /// <summary>
        /// 真跑一次对话（会消耗 token）
        /// </summary>
        public async Task<VivApiResult> ChatAsync(string agentKey, string text)
        {
            var agent = await _agents.GetAgentAsync(agentKey);
            if (agent is null) return VivApiResult.Failed($"Agent 未就绪：{agentKey}");

            var response = await agent.RunAsync(text);
            return VivApiResult.Success(new ChatTurnOutput
            {
                Text = response.Text,
                InputTokens = (int?)response.Usage?.InputTokenCount,
                OutputTokens = (int?)response.Usage?.OutputTokenCount
            });
        }

        /// <summary>
        /// 清缓存：改完库里的档位或 Agent 定义后调用，不必等 TTL
        /// </summary>
        public VivApiResult Refresh(string? agentKey, string? profileKey)
        {
            if (!string.IsNullOrWhiteSpace(agentKey)) _agents.Invalidate(agentKey);
            if (!string.IsNullOrWhiteSpace(profileKey)) _profiles.Invalidate(profileKey);

            return VivApiResult.Success();
        }
    }
}
