using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Viv.Apex.Core.Entity.Vo.User;
using Viv.Apex.Core.IService;
using Viv.Elysia.Request;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>
    /// 用户模块
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// 获取登录数据
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [ProducesResponseType(typeof(GetLoginDataOutput), (int)HttpStatusCode.OK)]
        [HttpPost("getLoginData")]
        public async Task<IActionResult> GetLoginDataAsync(ApiEmptyRequest request)
        {
            return await _userService.GetLoginDataAsync(request);
        }
    }
}
