using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Models;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>自检：档位解析 / Agent 装配 / 真实对话 / 清缓存 / 投递一轮到队列。返回统一信封。</summary>
    public interface IAgentDiagnosticsService
    {
        Task<VivApiResult> GetProfileAsync(string profileKey);

        Task<VivApiResult> GetAgentAsync(string agentKey);

        Task<VivApiResult> ChatAsync(string agentKey, string text);

        /// <summary>
        /// 落一条用户消息并写入发件箱（不在这里跑模型，跑的是 Worker）
        /// </summary>
        /// <param name="request">会话标识与用户输入</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<VivApiResult> QueueTurnAsync(QueueTurnRequest request, CancellationToken cancellationToken = default);

        VivApiResult Refresh(string? agentKey, string? profileKey);
    }
}
