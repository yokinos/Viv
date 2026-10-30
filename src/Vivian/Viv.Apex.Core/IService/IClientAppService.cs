using Viv.Apex.Core.Entity.Dto.App;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    /// <summary>客户端应用的后台查询。列表不含密钥。</summary>
    public interface IClientAppService
    {
        Task<VivApiResult> PageAsync(AppPageRequest request);
    }
}
