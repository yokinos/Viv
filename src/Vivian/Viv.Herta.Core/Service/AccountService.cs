using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Account.Output;
using Viv.Herta.Core.Entity.Dto.Account.Request;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Core.Service
{
    public class AccountService : IAccountService
    {
        public AccountService()
        {

        }

        public async Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request)
        {
            return VivApiResult<HertaLoginOutput>.Success();
        }
    }
}
