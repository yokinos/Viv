using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 模型档位管理：读写 OtModelProfile。密钥只进不出 —— 入参收明文，出参只有 hasKey。
    /// </summary>
    public interface IModelProfileAdminService
    {
        /// <summary>
        /// 分页列表
        /// </summary>
        Task<VivApiResult> ListAsync(string? profileKey, int pageIndex, int pageSize);

        /// <summary>
        /// 详情
        /// </summary>
        Task<VivApiResult> GetAsync(long id);

        /// <summary>
        /// 新增：明文密钥加密后写 ApiKeyCipher
        /// </summary>
        Task<VivApiResult> CreateAsync(CreateModelProfileRequest request);

        /// <summary>
        /// 修改：ApiKey 为 null 表示不动现有密钥
        /// </summary>
        Task<VivApiResult> UpdateAsync(UpdateModelProfileRequest request);

        /// <summary>
        /// 启用/停用；<paramref name="force"/> 为假时挡住"停掉唯一可用档位"
        /// </summary>
        Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force);
    }
}
