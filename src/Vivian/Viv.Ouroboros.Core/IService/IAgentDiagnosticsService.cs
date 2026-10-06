using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>自检：档位解析 / Agent 装配 / 真实对话 / 清缓存。返回统一信封。</summary>
    public interface IAgentDiagnosticsService
    {
        Task<VivApiResult> GetProfileAsync(string profileKey);

        Task<VivApiResult> GetAgentAsync(string agentKey);

        Task<VivApiResult> ChatAsync(string agentKey, string text);

        VivApiResult Refresh(string? agentKey, string? profileKey);
    }
}
