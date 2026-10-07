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
        private readonly IConfigChangeNotifier _notifier;

        public AgentDiagnosticsService(IModelProfileProvider profiles, IAgentFactory agents, IConfigChangeNotifier notifier)
        {
            _profiles = profiles;
            _agents = agents;
            _notifier = notifier;
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
        /// 清缓存：改完库里的档位、Agent 定义或工具绑定时调用，不必等 TTL。
        /// 与各管理接口的写操作走同一个失效入口（<see cref="IConfigChangeNotifier"/>），口径只有一份。
        /// </summary>
        public VivApiResult Refresh(string? agentKey, string? profileKey)
        {
            // 本进程先按范围清；不带 agentKey 时它内部会两边全清（Agent 装配里含着提示词与工具列表），
            // 再写共享版本戳让其它实例在节流窗口内自行清缓存
            _notifier.Notify(agentKey, profileKey);

            return VivApiResult.Success();
        }
    }
}
