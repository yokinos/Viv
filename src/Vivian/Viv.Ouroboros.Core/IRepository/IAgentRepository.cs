using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Database.Ouroboros;

namespace Viv.Ouroboros.Core.IRepository
{
    /// <summary>
    /// Agent 定义仓储（OtAgent / OtAgentPrompt / OtCapabilityBinding）。
    /// </summary>
    public interface IAgentRepository
    {
        /// <summary>按业务键取 Agent 定义（含未启用，由调用方判断）</summary>
        Task<OtAgent?> GetByKeyAsync(string agentKey);

        /// <summary>取指定版本的提示词正文；版本不存在返回 null</summary>
        Task<string?> GetPromptContentAsync(string agentKey, int version);

        /// <summary>取该 Agent 当前启用的能力绑定（工具 / 子 Agent / MCP）</summary>
        Task<List<OtCapabilityBinding>> GetCapabilitiesAsync(string agentKey);
    }
}
