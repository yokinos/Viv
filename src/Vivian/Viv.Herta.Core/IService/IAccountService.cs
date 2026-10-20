using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Entity.Vo.Account;

namespace Viv.Herta.Core.IService
{
    /// <summary>IM 自有账号体系，与平台用户表不相通</summary>
    public interface IAccountService
    {
        Task<VivApiResult> RegisterAsync(HertaRegisterRequest request);

        Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request);
    }
}
