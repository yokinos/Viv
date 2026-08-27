using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    public interface IUserService
    {
        Task<VivApiResult<LoginOutput>> LoginAsync(LoginRequest request);

        Task<VivApiResult<LoginOutput>> RefreshTokenAsync(RefreshRequest request);

        Task<VivApiResult> LogoutAsync(LoginoutRequest request);
    }
}
