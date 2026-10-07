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
        /// 落一条用户消息并投递跑轮事件（不在这里跑模型，跑的是 Worker）
        /// </summary>
        /// <param name="request">会话标识与用户输入</param>
        /// <param name="identity">
        /// 调用者身份。本接口挂在 [AllowAnonymous] 控制器上，框架的 VivContextMiddleware 对匿名端点
        /// 整个跳过水合，所以 IVivContext 到 Service 时全是 0 —— 身份只能由控制器从当前请求的
        /// JWT 取来传进来，否则投出去的事件没有主体，Worker 侧会话越权校验会拒掉每一轮。
        /// </param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<VivApiResult> QueueTurnAsync(QueueTurnRequest request, VivContextContent? identity, CancellationToken cancellationToken = default);

        VivApiResult Refresh(string? agentKey, string? profileKey);
    }
}
