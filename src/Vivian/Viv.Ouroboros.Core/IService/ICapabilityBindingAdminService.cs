using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 能力绑定管理：读写 OtCapabilityBinding。能力键必须指向真实存在的能力，否则报了也调不到。
    /// </summary>
    public interface ICapabilityBindingAdminService
    {
        /// <summary>
        /// 分页列表
        /// </summary>
        Task<VivApiResult> ListAsync(string? agentKey, int? capabilityType, int pageIndex, int pageSize);

        /// <summary>
        /// 详情
        /// </summary>
        Task<VivApiResult> GetAsync(long id);

        /// <summary>
        /// 新增：宿主 Agent、能力键、白名单、暴露名冲突、子 Agent 环全部校验
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateCapabilityBindingRequest request);

        /// <summary>
        /// 修改：AgentKey 不可改，其余按新增同一套规则校验
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateCapabilityBindingRequest request);

        /// <summary>
        /// 启用/停用
        /// </summary>
        Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled);
    }
}
