using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.IService;
using Viv.Elysia.Attributes;
using Viv.Elysia.Logging;
using Viv.Entity.Enums;

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
        private readonly IOperationLogScope _operationLog;

        public AccountController(IAccountService accountService, IOperationLogScope operationLog)
        {
            _accountService = accountService;
            _operationLog = operationLog;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [OperationLog(EmOperationType.Login, Name = "登录")]
        [ProducesResponseType(typeof(LoginOutput), StatusCodes.Status200OK)]
        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync(LoginRequest request)
        {
            var result = await _accountService.LoginAsync(request);

            // 登录请求本身没有登录态，UserId 取不到；但要审计的对象是这个账号，从结果里补
            if (result.Data?.UserId is > 0)
            {
                _operationLog.Target("User", result.Data.UserId);
            }

            return result;
        }

        /// <summary>
        /// 刷新登录令牌
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginOutput), StatusCodes.Status200OK)]
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
        [OperationLog(EmOperationType.Logout, Name = "退出登录")]
        [HttpPost("logout")]
        public async Task<IActionResult> LogoutAsync(LoginoutRequest request)
        {
            return await _accountService.LogoutAsync(request);
        }
    }
}
