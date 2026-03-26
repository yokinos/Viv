using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Entity.ViewModel.Account.Request;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    public interface IUserService
    {
        Task<VivApiResult> LoginAsync(ApexLoginRequest request);
    }
}
