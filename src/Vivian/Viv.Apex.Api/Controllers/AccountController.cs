using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.IService;
using Viv.Elysia.Filter;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>
    /// 用户登录账号模块
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IAccountService _accountService;

        public AccountController(IAccountService accountService)
        {
            _accountService = accountService;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginOutput), (int)HttpStatusCode.OK)]
        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync(LoginRequest request)
        {
            return await _accountService.LoginAsync(request);
        }

        /// <summary>
        /// 刷新登录令牌
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginOutput), (int)HttpStatusCode.OK)]
        [HttpPost("refreshToken")]
        public async Task<IActionResult> RefreshTokenAsync(RefreshRequest request)
        {
            return await _accountService.RefreshTokenAsync(request);
        }

        /// <summary>
        /// 退出登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> LogoutAsync(LoginoutRequest request)
        {
            return await _accountService.LogoutAsync(request);
        }
    }
}
