using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Account;
using Viv.Herta.Core.Entity.Vo.Account;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IAccountService _accountService;

        public AccountController(IAccountService accountService)
        {
            _accountService = accountService;
        }

        [AllowAnonymous]
        [HttpPost("hertaLogin")]
        public async Task<VivApiResult<HertaLoginOutput>> HertaLoginAsync(HertaLoginRequest request)
        {
            return await _accountService.HertaLoginAsync(request);
        }
    }
}
