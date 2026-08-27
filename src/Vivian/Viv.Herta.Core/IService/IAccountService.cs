using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Entity.Vo.Account;

namespace Viv.Herta.Core.IService
{
    public interface IAccountService
    {
        Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request);
    }
}
