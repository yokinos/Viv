using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>后台菜单节点。含停用行，路由配置保持入库原文，不走运行时适配器改写。</summary>
    public class MenuAdminNode
    {
        public long Id { get; set; }

        public long ParentId { get; set; }

        public string Title { get; set; } = string.Empty;

        public EmMenuType Type { get; set; }

        public string? Icon { get; set; }

        public int Sort { get; set; }

        public bool IsVisible { get; set; }

        public string? RouteConfig { get; set; }

        public string? Description { get; set; }

        public EmStatus Status { get; set; }

        public List<MenuAdminNode> Children { get; set; } = [];
    }
}
