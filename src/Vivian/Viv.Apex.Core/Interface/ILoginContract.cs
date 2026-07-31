using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Delusion;

namespace Viv.Apex.Core.Interface
{
    public interface ILoginContract
    {
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request);

        /// <summary>
        /// 刷新Token
        /// </summary>
        /// <param name="request"></param>
        Task<FuncResult<ApexLoginOutput>> RefreshTokenAsync(ApexRefreshRequest request);

        /// <summary>
        /// 退出登录
        /// <param name="request"></param>
        /// <returns></returns>
        Task<bool> LogoutAsync(ApexLoginoutRequest request);
    }
}
