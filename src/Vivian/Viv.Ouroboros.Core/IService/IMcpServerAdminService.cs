using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// MCP 服务管理：读写 OtMcpServer。只做注册，MCP 工具执行仍未实现。
    /// </summary>
    public interface IMcpServerAdminService
    {
        /// <summary>
        /// 分页列表
        /// </summary>
        Task<VivApiResult> ListAsync(string? serverName, int pageIndex, int pageSize);

        /// <summary>
        /// 详情
        /// </summary>
        Task<VivApiResult> GetAsync(long id);

        /// <summary>
        /// 新增：ServerName 查重，几个 JSON 列都要合法
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateMcpServerRequest request);

        /// <summary>
        /// 修改：ServerName 不可改；Headers 为 null 表示不动现有请求头
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateMcpServerRequest request);

        /// <summary>
        /// 启用/停用；<paramref name="force"/> 为假时挡住"停用还被绑定引用的 MCP 服务"
        /// </summary>
        Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force);
    }
}
