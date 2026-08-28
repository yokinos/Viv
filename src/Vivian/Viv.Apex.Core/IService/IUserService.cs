using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Dto.User;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    public interface IUserService
    {
        Task<VivApiResult> GetLoginDataAsync(ApiEmptyRequest request);
    }
}
