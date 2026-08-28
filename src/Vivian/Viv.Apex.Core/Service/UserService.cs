using Autofac.Features.Indexed;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Dto.User;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Entity.Vo.User;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IService;
using Viv.Elysia;
using Viv.Elysia.Request;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Service
{
    public class UserService : IUserService
    {
        public UserService()
        {

        }

        public async Task<VivApiResult> GetLoginDataAsync(ApiEmptyRequest request)
        {
            var output = new GetLoginDataOutput();
            return VivApiResult.Success(output);
        }

        public async Task<VivApiResult> GetUserAsync(GetUserRequest request)
        {
            return VivApiResult.Success();
        }
    }
}
