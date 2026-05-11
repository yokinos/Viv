using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    public interface IUserService
    {
        Task<VivApiResult> LoginAsync(ApexLoginRequest request);
    }
}
