using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.Entity.Vo.App;
using Viv.Apex.Core.Entity.Vo.Menu;
using Viv.Apex.Core.IService;
using Viv.Delusion.Generic;
using Viv.Elysia.Request;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>
    /// 客户端应用后台模块：应用分页/维护，以及某个应用下的菜单、子页面与动作
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AppController : ControllerBase
    {
        private readonly IClientAppService _clientAppService;
        private readonly IMenuService _menuService;

        public AppController(IClientAppService clientAppService, IMenuService menuService)
        {
            _clientAppService = clientAppService;
            _menuService = menuService;
        }

        /// <summary>
        /// 应用分页。关键字匹配名称或 AppKey，响应不含密钥
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [ProducesResponseType(typeof(PagedList<ClientAppListItem>), StatusCodes.Status200OK)]
        [HttpPost("getAppPage")]
        public async Task<IActionResult> GetAppPageAsync(AppPageRequest request)
        {
            return await _clientAppService.GetPageAsync(request);
        }

        /// <summary>
        /// 新增或更新应用：Id 为 0 是新增，密钥只在新增时返回一次
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [ProducesResponseType(typeof(SaveAppOutput), StatusCodes.Status200OK)]
        [HttpPost("saveApp")]
        public async Task<IActionResult> SaveAppAsync(SaveAppRequest request)
        {
            return await _clientAppService.SaveAsync(request);
        }

        /// <summary>
        /// 按 Id 取单个应用（不含密钥）
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [ProducesResponseType(typeof(ClientAppListItem), StatusCodes.Status200OK)]
        [HttpPost("getApp")]
        public async Task<IActionResult> GetAppAsync(ApiIdRequest request)
        {
            return await _clientAppService.GetAppAsync(request);
        }

        /// <summary>
        /// 指定应用的上级菜单候选项（只带 Id/上级/名称/排序/类型）
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [ProducesResponseType(typeof(List<MenuOption>), StatusCodes.Status200OK)]
        [HttpPost("getMenuOptions")]
        public async Task<IActionResult> GetMenuOptionsAsync(ApiIdRequest request)
        {
            return await _menuService.GetMenuOptionsAsync(request);
        }

        /// <summary>
        /// 指定应用的菜单分页，行上带子页面与动作
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [ProducesResponseType(typeof(PagedList<AppMenuRow>), StatusCodes.Status200OK)]
        [HttpPost("getMenuPage")]
        public async Task<IActionResult> GetMenuPageAsync(AppMenuPageRequest request)
        {
            return await _menuService.GetAdminPageAsync(request);
        }

        /// <summary>
        /// 给指定应用新增或更新菜单：Id 为 0 是新增
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("saveMenu")]
        public async Task<IActionResult> SaveMenuAsync(SaveAppMenuRequest request)
        {
            return await _menuService.SaveForAppAsync(request);
        }

        /// <summary>
        /// 删除指定应用下的菜单：软删菜单本身、子页面与动作；有子菜单时拒绝
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("deleteMenu")]
        public async Task<IActionResult> DeleteMenuAsync(DeleteAppMenuRequest request)
        {
            return await _menuService.DeleteForAppAsync(request);
        }

        /// <summary>
        /// 给指定应用下的页面菜单新增或更新动作（权限点）：Id 为 0 是新增
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("saveMenuAction")]
        public async Task<IActionResult> SaveMenuActionAsync(SaveAppMenuActionRequest request)
        {
            return await _menuService.SaveActionForAppAsync(request);
        }

        /// <summary>
        /// 删除指定应用下的菜单动作（权限点）
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("deleteMenuAction")]
        public async Task<IActionResult> DeleteMenuActionAsync(DeleteAppMenuActionRequest request)
        {
            return await _menuService.DeleteActionForAppAsync(request);
        }

        /// <summary>
        /// 给指定应用下的页面菜单新增或更新子页面：Id 为 0 是新增
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("saveMenuSubPage")]
        public async Task<IActionResult> SaveMenuSubPageAsync(SaveAppMenuSubPageRequest request)
        {
            return await _menuService.SaveSubPageForAppAsync(request);
        }

        /// <summary>
        /// 删除指定应用下的子页面
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("deleteMenuSubPage")]
        public async Task<IActionResult> DeleteMenuSubPageAsync(DeleteAppMenuSubPageRequest request)
        {
            return await _menuService.DeleteSubPageForAppAsync(request);
        }
    }
}
