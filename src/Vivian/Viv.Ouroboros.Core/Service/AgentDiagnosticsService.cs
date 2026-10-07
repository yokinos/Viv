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
        private readonly IToolRegistry _tools;
        private readonly IConfigVersionGate _version;

        public AgentDiagnosticsService(IModelProfileProvider profiles, IAgentFactory agents, IToolRegistry tools,
            IConfigVersionGate version)
        {
            _profiles = profiles;
            _agents = agents;
            _tools = tools;
            _version = version;
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
        /// 清缓存：改完库里的档位、Agent 定义或工具绑定时调用，不必等 TTL
        /// </summary>
        public VivApiResult Refresh(string? agentKey, string? profileKey)
        {
            if (!string.IsNullOrWhiteSpace(agentKey))
            {
                _agents.Invalidate(agentKey);
                _tools.Invalidate(agentKey);
            }
            else
            {
                // 没指定 Agent 就两边全清：Agent 装配里含着提示词与工具列表，只清工具会留下"Agent 还是旧的"
                // —— 那条路径下改了提示词/档位必须重启才生效，等于 refresh 没做事
                _agents.InvalidateAll();
                _tools.InvalidateAll();
            }

            if (!string.IsNullOrWhiteSpace(profileKey)) _profiles.Invalidate(profileKey);

            // 本进程上面已经清完了；这一步是给**其它实例**看的：写共享版本戳，它们在节流窗口内自行清缓存。
            // 版本戳只有一个（不带 agentKey），所以带 agentKey 的 refresh 在别处会退化成全清 —— 宁可多清一次。
            _version.Publish();

            return VivApiResult.Success();
        }
    }
}
