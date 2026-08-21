using Autofac.Features.Indexed;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IService;
using Viv.Elysia;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    public class UserService : IUserService
    {
        private readonly IIndex<EmUserType, ILoginContract> _loginImpls;

        public UserService(IIndex<EmUserType, ILoginContract> loginImpls)
        {
            _loginImpls = loginImpls;
        }

        public async Task<VivApiResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Login);
            var isExist = _loginImpls.TryGetValue(request.UserType, out var loginImpl);
            if (!isExist || loginImpl == null)
            {
                return VivApiResult<ApexLoginOutput>.Failed("未知的用户类型");
            }

            var loginResult = await loginImpl.LoginAsync(request);
            if (!loginResult.IsSuccess)
            {
                return VivApiResult<ApexLoginOutput>.Failed(loginResult.Message);
            }

            return VivApiResult<ApexLoginOutput>.Success("Login successful", loginResult.Data);
        }
    }
}
