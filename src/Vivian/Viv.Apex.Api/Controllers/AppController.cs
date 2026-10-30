using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Apex.Core.Entity.Dto.App;
using Viv.Apex.Core.IService;
using Viv.Engine;

namespace Viv.Apex.Api.Controllers
{
    /// <summary>
    /// 客户端应用后台。列表进入某个应用后，菜单的增删改都带上该应用的 Id，
    /// 不使用登录上下文里的 AppId。
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AppController : ControllerBase
    {
        private readonly IClientAppService _apps;
        private readonly IMenuService _menus;

        public AppController(IClientAppService apps, IMenuService menus)
        {
            _apps = apps;
            _menus = menus;
        }

        /// <summary>应用分页。关键字匹配名称或 AppKey，响应不含密钥</summary>
        [HttpPost("page")]
        public Task<VivApiResult> PageAsync(AppPageRequest request)
            => _apps.PageAsync(request);

        /// <summary>指定应用的菜单树，含停用节点</summary>
        [HttpPost("menus")]
        public Task<VivApiResult> MenusAsync(AppMenuTreeRequest request)
            => _menus.GetAdminTreeAsync(request);

        /// <summary>给指定应用新增或更新菜单</summary>
        [HttpPost("menu/save")]
        public Task<VivApiResult> SaveMenuAsync(SaveAppMenuRequest request)
            => _menus.SaveForAppAsync(request);

        /// <summary>删除指定应用下的菜单；有子菜单时拒绝</summary>
        [HttpPost("menu/delete")]
        public Task<VivApiResult> DeleteMenuAsync(DeleteAppMenuRequest request)
            => _menus.DeleteForAppAsync(request);
    }
}
