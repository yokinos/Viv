using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Account.Output;
using Viv.Herta.Core.Entity.Dto.Account.Request;

namespace Viv.Herta.Core.IService
{
    public interface IAccountService
    {
        Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request);
    }
}
