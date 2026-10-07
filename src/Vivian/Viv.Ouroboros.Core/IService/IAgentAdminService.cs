using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// Agent 管理：读写 OtAgent（含档位引用与生效提示词版本）。
    /// </summary>
    public interface IAgentAdminService
    {
        /// <summary>
        /// 分页列表
        /// </summary>
        Task<VivApiResult> ListAsync(string? agentKey, int? agentType, string? ownerDomain, int pageIndex, int pageSize);

        /// <summary>
        /// 详情
        /// </summary>
        Task<VivApiResult> GetAsync(long id);

        /// <summary>
        /// 新增：AgentKey 查重，档位与提示词版本都要真实存在
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateAgentRequest request);

        /// <summary>
        /// 修改：AgentKey 不可改
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateAgentRequest request);

        /// <summary>
        /// 启用/停用；<paramref name="force"/> 为假时挡住"停用还被别人绑着的子 Agent"
        /// </summary>
        Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force);

        /// <summary>
        /// 软删：只置 IsDeleted，另有启用的会话或绑定在用时拒绝
        /// </summary>
        Task<VivApiResult> DeleteAsync(long id);
    }
}
