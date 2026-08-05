using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.Interface;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Impl.Login
{
    [VivDependency(Tag = EmUserType.CompanyUser)]
    public class CompanyUserLoginImpl : ILoginContract, IDependency
    {
        public Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<bool> LogoutAsync(ApexLoginoutRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<FuncResult<ApexLoginOutput>> RefreshTokenAsync(ApexRefreshRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
