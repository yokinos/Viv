using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 工具管理：读写 OtTool（工具注册表，也是权限边界的一半）。
    /// </summary>
    public interface IToolAdminService
    {
        /// <summary>
        /// 分页列表
        /// </summary>
        Task<VivApiResult> ListAsync(string? toolKey, string? ownerDomain, int? transport, int pageIndex, int pageSize);

        /// <summary>
        /// 详情
        /// </summary>
        Task<VivApiResult> GetAsync(long id);

        /// <summary>
        /// 新增：ToolKey 查重、ParamsSchema 必须是合法 JSON 对象
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateToolRequest request);

        /// <summary>
        /// 修改：ToolKey 不可改，ParamsSchema 同样要过校验
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateToolRequest request);

        /// <summary>
        /// 启用/停用；<paramref name="force"/> 为假时挡住"停用还被绑定引用的工具"
        /// </summary>
        Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force);
    }
}
