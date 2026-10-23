using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Apex.Core.IService;
using Viv.Engine;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>客户端应用相关：菜单与路由属于 App 的能力，按登录身份的 AppId 取</summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AppController : ControllerBase
    {
        private readonly IMenuService _menu;

        public AppController(IMenuService menu)
        {
            _menu = menu;
        }

        /// <summary>
        /// 当前客户端的路由树。结构由客户端技术栈决定（Vue 是嵌套树），后端已组好，前端可直接注册
        /// </summary>
        [HttpGet("menu/tree")]
        [ProducesResponseType(typeof(VivApiResult<object>), (int)HttpStatusCode.OK)]
        public async Task<VivApiResult> GetMenuTreeAsync()
        {
            return await _menu.GetRouteTreeAsync();
        }
    }
}
