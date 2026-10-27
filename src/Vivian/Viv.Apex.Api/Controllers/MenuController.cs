using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using Viv.Apex.Core.Entity.Dto.Menu;
using Viv.Apex.Core.IService;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>菜单模块：路由树下发与菜单维护，全部按登录身份的 AppId 归属</summary>
    [Route("api/menu")]
    [ApiController]
    [Authorize]
    public class MenuController : ControllerBase
    {
        private readonly IMenuService _menu;

        public MenuController(IMenuService menu)
        {
            _menu = menu;
        }

        /// <summary>
        /// 当前客户端的路由树。结构由客户端技术栈决定（Vue 是嵌套树），后端已组好，前端可直接注册
        /// </summary>
        [HttpGet("tree")]
        [ProducesResponseType(typeof(VivApiResult<object>), StatusCodes.Status200OK)]
        public async Task<VivApiResult> GetTreeAsync()
        {
            return await _menu.GetRouteTreeAsync();
        }

        /// <summary>新增或更新菜单：Id 为 0 是新增。路由配置按当前客户端技术栈校验</summary>
        [HttpPost("save")]
        public async Task<VivApiResult> SaveAsync([FromBody] SaveMenuRequest request)
        {
            return await _menu.SaveAsync(request);
        }

        /// <summary>删除菜单：软删菜单本身与它的子页面；有子菜单时拒绝</summary>
        [HttpPost("delete")]
        public async Task<VivApiResult> DeleteAsync(ApiIdRequest request)
        {
            return await _menu.DeleteAsync(request);
        }
    }
}
