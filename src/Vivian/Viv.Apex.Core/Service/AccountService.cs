using Autofac.Features.Indexed;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IService;
using Viv.Elysia;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    public class AccountService : IAccountService
    {
        private readonly IIndex<EmUserType, ILoginContract> _loginImpls;

        public AccountService(IIndex<EmUserType, ILoginContract> loginImpls)
        {
            _loginImpls = loginImpls;
        }

        public async Task<VivApiResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
            ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Login);
            var isExist = _loginImpls.TryGetValue(request.UserType, out var loginImpl);
            if (!isExist || loginImpl == null)
            {
                return VivApiResult<LoginOutput>.Failed("未知的用户类型");
            }

            var result = await loginImpl.LoginAsync(request);
            if (!result.IsSuccess)
            {
                return VivApiResult<LoginOutput>.Failed(result.Message);
            }

            return VivApiResult<LoginOutput>.Success("登录成功", result.Data);
        }

        public async Task<VivApiResult> LogoutAsync(LoginoutRequest request)
        {
            var isExist = _loginImpls.TryGetValue(request.UserType, out var loginImpl);
            if (!isExist || loginImpl == null)
            {
                return VivApiResult<LoginOutput>.Failed("未知的用户类型");
            }

            var flag = await loginImpl.LogoutAsync(request);
            if (flag)
            {
                ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Logout);
            }

            return VivApiResult<LoginOutput>.Success("退出登录成功");
        }

        public async Task<VivApiResult<LoginOutput>> RefreshTokenAsync(RefreshRequest request)
        {
            var isExist = _loginImpls.TryGetValue(request.UserType, out var loginImpl);
            if (!isExist || loginImpl == null)
            {
                return VivApiResult<LoginOutput>.Failed("未知的用户类型");
            }

            var result = await loginImpl.RefreshTokenAsync(request);
            if (!result.IsSuccess)
            {
                return VivApiResult<LoginOutput>.Failed(result.Message);
            }

            return VivApiResult<LoginOutput>.Success("刷新成功", result.Data);
        }
    }
}
