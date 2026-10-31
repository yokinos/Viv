using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>后台菜单节点。含停用行，路由配置保持入库原文，不走运行时适配器改写。</summary>
    public class MenuAdminNode
    {
        /// <summary>节点 Id。菜单是 AtMenu.Id，子页面是 AtMenuSubPage.Id。</summary>
        public long Id { get; set; }

        /// <summary>父节点 Id。顶级为 0。子页面的父级是它所属的菜单。</summary>
        public long ParentId { get; set; }

        /// <summary>显示名称。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>目录或页面。子页面固定为页面。</summary>
        public EmMenuType Type { get; set; }

        /// <summary>图标标识。</summary>
        public string? Icon { get; set; }

        /// <summary>排序号，越小越靠前。</summary>
        public int Sort { get; set; }

        /// <summary>是否在菜单里展示。隐藏的子页面路由仍然注册。</summary>
        public bool IsVisible { get; set; }

        /// <summary>路由配置 JSON，保持入库原文。</summary>
        public string? RouteConfig { get; set; }

        /// <summary>备注。子页面取的是 AtMenuSubPage.Remark。</summary>
        public string? Description { get; set; }

        /// <summary>启用或禁用。Enabled = 0。</summary>
        public EmStatus Status { get; set; }

        /// <summary>挂在页面下的子页面，不是菜单节点。这类行不能按菜单保存或删除。</summary>
        public bool IsSubPage { get; set; }

        /// <summary>页面下的动作（权限点）。目录与子页面为空。</summary>
        public List<MenuAdminAction> Actions { get; set; } = [];

        /// <summary>子节点。先是下级菜单，再是这个页面自己的子页面。</summary>
        public List<MenuAdminNode> Children { get; set; } = [];
    }
}
