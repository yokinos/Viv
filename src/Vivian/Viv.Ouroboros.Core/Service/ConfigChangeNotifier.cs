using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 配置失效通知实现：本进程按范围清缓存，再广播共享版本戳。
    ///
    /// 为什么清完本地还要 Publish：装配好的 Agent 里嵌着子 Agent 与工具闭包，改一个子 Agent
    /// 会让所有绑它的父 Agent 一起过期，靠本方法回推整张依赖图不划算；把版本推高一格，
    /// 各缓存下一次取用时自己全清（本地如此，其它实例在节流窗口内跟上）。
    ///
    /// 实现 <see cref="IDependency"/> 走自动注册（类名不以 Service 结尾，DIOption 的后缀扫描扫不到它）。
    /// </summary>
    public class ConfigChangeNotifier : IConfigChangeNotifier, IDependency
    {
        private readonly IAgentFactory _agents;
        private readonly IToolRegistry _tools;
        private readonly IModelProfileProvider _profiles;
        private readonly IConfigVersionGate _version;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="agents">Agent 装配缓存</param>
        /// <param name="tools">工具装配缓存</param>
        /// <param name="profiles">档位解析缓存（它不看版本戳，只能点名清）</param>
        /// <param name="version">配置版本闸门（跨实例广播）</param>
        public ConfigChangeNotifier(IAgentFactory agents, IToolRegistry tools, IModelProfileProvider profiles, IConfigVersionGate version)
        {
            _agents = agents;
            _tools = tools;
            _profiles = profiles;
            _version = version;
        }

        /// <inheritdoc />
        public void Notify(string? agentKey = null, string? profileKey = null)
        {
            if (!string.IsNullOrWhiteSpace(agentKey))
            {
                _agents.Invalidate(agentKey);
                _tools.Invalidate(agentKey);
            }
            else
            {
                // 范围说不准就两边全清：Agent 装配里含着提示词、工具列表与子 Agent，只清一边会留下"半新半旧"
                _agents.InvalidateAll();
                _tools.InvalidateAll();
            }

            // 档位缓存没有版本戳兜底（ModelProfileProvider 不读闸门），必须点名清
            if (!string.IsNullOrWhiteSpace(profileKey)) _profiles.Invalidate(profileKey);

            _version.Publish();
        }
    }
}
