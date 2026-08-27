using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Delusion;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Interface
{
    public interface ILoginContract
    {
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request);

        /// <summary>
        /// 刷新Token
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<FuncResult<LoginOutput>> RefreshTokenAsync(RefreshRequest request);

        /// <summary>
        /// 退出登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<bool> LogoutAsync(LoginoutRequest request);
    }
}
