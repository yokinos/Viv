using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    /// <summary>菜单服务：把菜单表组装成当前客户端能直接用的路由树</summary>
    public interface IMenuService
    {
        /// <summary>
        /// 取当前登录身份所属客户端应用的路由树。
        /// 后端负责组树、排序、补默认值，前端拿到直接用。
        /// </summary>
        Task<VivApiResult> GetRouteTreeAsync();
    }
}
