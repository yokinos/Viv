using Viv.Apex.Core.Entity.Vo.Menu;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.App
{
    /// <summary>菜单分页的一行。子页面与动作直接挂在行上，供页面上的「子页面 / 动作」抽屉使用。</summary>
    public class AppMenuRow
    {
        /// <summary>菜单 Id。</summary>
        public long Id { get; set; }

        /// <summary>父菜单 Id，顶级为 0。</summary>
        public long ParentId { get; set; }

        /// <summary>父菜单名称。顶级为空。</summary>
        public string ParentTitle { get; set; } = string.Empty;

        /// <summary>菜单名称。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>目录或页面。</summary>
        public EmMenuType Type { get; set; }

        /// <summary>图标标识。</summary>
        public string? Icon { get; set; }

        /// <summary>排序号，越小越靠前。</summary>
        public int Sort { get; set; }

        /// <summary>是否在菜单里展示。</summary>
        public bool IsVisible { get; set; }

        /// <summary>路由配置 JSON，保持入库原文。</summary>
        public string? RouteConfig { get; set; }

        /// <summary>备注（仅后台使用）。</summary>
        public string? Description { get; set; }

        /// <summary>启用或禁用。</summary>
        public EmStatus Status { get; set; }

        /// <summary>下级菜单数量。有下级时不允许删除。</summary>
        public int ChildCount { get; set; }

        /// <summary>页面下的子页面（目录为空）。</summary>
        public List<MenuSubPageAdmin> SubPages { get; set; } = [];

        /// <summary>页面下的动作（目录为空）。</summary>
        public List<MenuAdminAction> Actions { get; set; } = [];
    }
}
