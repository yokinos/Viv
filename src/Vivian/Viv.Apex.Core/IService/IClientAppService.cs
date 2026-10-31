using Viv.Apex.Core.Entity.Dto.App;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    /// <summary>客户端应用的后台查询。列表不含密钥。</summary>
    public interface IClientAppService
    {
        Task<VivApiResult> PageAsync(AppPageRequest request);

        /// <summary>新增或更新应用。密钥由服务生成，只在新增的返回里出现一次。</summary>
        Task<VivApiResult> SaveAsync(SaveAppRequest request);
    }
}
