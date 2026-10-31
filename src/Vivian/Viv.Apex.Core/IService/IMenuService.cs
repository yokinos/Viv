using Viv.Apex.Core.Entity.Dto.App;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    /// <summary>菜单服务：路由树下发与后台菜单维护</summary>
    public interface IMenuService
    {
        /// <summary>
        /// 取当前登录身份所属客户端应用的路由树。
        /// 登录时随 getLoginData 一起下发，后端负责组树、排序、补默认值。
        /// </summary>
        Task<VivApiResult> GetRouteTreeAsync();

        /// <summary>指定应用的菜单维护树（含停用）。只给超级管理员</summary>
        Task<VivApiResult> GetAdminTreeAsync(AppMenuTreeRequest request);

        /// <summary>指定应用的菜单分页，行上带子页面与动作。只给超级管理员</summary>
        Task<VivApiResult> GetAdminPageAsync(AppMenuPageRequest request);

        /// <summary>给指定应用新增或更新菜单。只给超级管理员</summary>
        Task<VivApiResult> SaveForAppAsync(SaveAppMenuRequest request);

        /// <summary>删除指定应用下的菜单。只给超级管理员</summary>
        Task<VivApiResult> DeleteForAppAsync(DeleteAppMenuRequest request);

        /// <summary>给指定应用下的页面菜单新增或更新动作（权限点）。只给超级管理员</summary>
        Task<VivApiResult> SaveActionForAppAsync(SaveAppMenuActionRequest request);

        /// <summary>删除指定应用下的菜单动作。只给超级管理员</summary>
        Task<VivApiResult> DeleteActionForAppAsync(DeleteAppMenuActionRequest request);

        /// <summary>给指定应用下的页面菜单新增或更新子页面。只给超级管理员</summary>
        Task<VivApiResult> SaveSubPageForAppAsync(SaveAppMenuSubPageRequest request);

        /// <summary>删除指定应用下的子页面。只给超级管理员</summary>
        Task<VivApiResult> DeleteSubPageForAppAsync(DeleteAppMenuSubPageRequest request);
    }
}
