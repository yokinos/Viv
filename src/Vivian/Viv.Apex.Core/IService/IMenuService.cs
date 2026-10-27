using Viv.Apex.Core.Entity.Dto.Menu;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    /// <summary>菜单服务：路由树的下发与菜单的增删改</summary>
    public interface IMenuService
    {
        /// <summary>
        /// 取当前登录身份所属客户端应用的路由树。
        /// 后端负责组树、排序、补默认值，前端拿到直接用。
        /// </summary>
        Task<VivApiResult> GetRouteTreeAsync();

        /// <summary>新增或更新菜单（Id 为 0 是新增）；路由配置按客户端技术栈校验</summary>
        Task<VivApiResult> SaveAsync(SaveMenuRequest request);

        /// <summary>软删除菜单及其子页面；有子菜单时拒绝</summary>
        Task<VivApiResult> DeleteAsync(ApiIdRequest request);
    }
}
