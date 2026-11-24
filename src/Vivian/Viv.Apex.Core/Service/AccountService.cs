using Autofac.Features.Indexed;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.IService;
using Viv.Apex.Core.Module.Login;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    public class AccountService : IAccountService
    {
        private readonly IIndex<EmUserType, ILoginContract> _loginImpls;
        private readonly IDistributedLock _distributedLock;

        public AccountService(IIndex<EmUserType, ILoginContract> loginImpls, IDistributedLock distributedLock)
        {
            _loginImpls = loginImpls;
            _distributedLock = distributedLock;
        }

        public async Task<VivApiResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
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

            // 审计标注在 AccountController.LoginAsync 上：失败登录也要留痕，
            // 所以不能像原来那样"成功之后在 Service 里补一句"。
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
            if (!flag)
            {
                return VivApiResult<LoginOutput>.Failed("退出登录失败，请重试");
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
