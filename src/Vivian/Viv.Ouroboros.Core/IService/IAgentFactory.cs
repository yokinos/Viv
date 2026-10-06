using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Agents.AI;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// Agent 工厂：把库里的 Agent 定义（提示词 + 模型档位 + 能力绑定）装配成可运行的 <see cref="AIAgent"/>。
    /// 定义全部来自数据库 —— 有几个主 Agent、挂哪些子 Agent、用哪个模型，改库即可，不发版。
    /// </summary>
    public interface IAgentFactory
    {
        /// <summary>按业务键取 Agent；未定义 / 未启用 / 无提示词 / 档位不可用都返回 null</summary>
        Task<AIAgent?> GetAgentAsync(string agentKey);

        /// <summary>清掉某个 Agent 的装配缓存（配置改动后调用）</summary>
        void Invalidate(string agentKey);
    }
}
