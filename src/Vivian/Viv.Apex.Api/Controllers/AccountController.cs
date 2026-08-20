using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.IService;
using Viv.Elysia.Filter;

namespace Viv.Apex.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IUserService _userService;

        public AccountController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApexLoginOutput), (int)HttpStatusCode.OK)]
        [HttpPost("apexLogin")]
        public async Task<IActionResult> ApexLoginAsync(ApexLoginRequest request)
        {
            return await _userService.LoginAsync(request);
        }
    }
}
