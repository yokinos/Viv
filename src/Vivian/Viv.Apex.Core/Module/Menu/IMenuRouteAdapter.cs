using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Module.Menu
{
    /// <summary>
    /// 菜单 → 客户端路由的适配器。一端的路由结构长什么样由实现自己决定：
    /// Vue 是嵌套的 route 树，小程序可能是平铺的 pages 数组，所以「组树」也归实现管，
    /// 接口只约定「给你菜单和子页面，还我一个可以直接下发的对象」。
    /// </summary>
    public interface IMenuRouteAdapter
    {
        /// <summary>负责的客户端技术栈，也是 DI 里的 key</summary>
        EmClientStack Stack { get; }

        /// <summary>组装该端可直接下发的路由数据</summary>
        object BuildTree(IReadOnlyList<AtMenu> menus, IReadOnlyList<AtMenuSubPage> subPages);
    }
}
