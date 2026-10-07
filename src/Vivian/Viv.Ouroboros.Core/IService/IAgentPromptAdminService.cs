using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 提示词管理：读写 OtAgentPrompt。新增只落版本，生效版本由 activate 显式切换。
    /// </summary>
    public interface IAgentPromptAdminService
    {
        /// <summary>
        /// 分页列表（按 AgentKey 过滤），不回正文
        /// </summary>
        Task<VivApiResult> ListAsync(string? agentKey, int pageIndex, int pageSize);

        /// <summary>
        /// 详情：按 AgentKey + Version 取
        /// </summary>
        Task<VivApiResult> GetAsync(string agentKey, int version);

        /// <summary>
        /// 新增一个版本，不自动生效
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateAgentPromptRequest request);

        /// <summary>
        /// 修改正文；当前生效的那一版不许改
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateAgentPromptRequest request);

        /// <summary>
        /// 切换 Agent 的生效提示词版本
        /// </summary>
        Task<VivApiResult> ActivateAsync(ActivateAgentPromptRequest request);

        /// <summary>
        /// 删除某个版本；当前生效的那一版不许删
        /// </summary>
        Task<VivApiResult> DeleteAsync(long id);
    }
}
