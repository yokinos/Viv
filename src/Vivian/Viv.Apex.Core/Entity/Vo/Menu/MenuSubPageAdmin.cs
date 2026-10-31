using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>后台子页面。子页面不能独立存在，永远挂在某个页面菜单下。</summary>
    public class MenuSubPageAdmin
    {
        /// <summary>子页面 Id（AtMenuSubPage.Id）</summary>
        public long Id { get; set; }

        /// <summary>所属页面菜单 Id</summary>
        public long MenuId { get; set; }

        /// <summary>显示名称</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>是否在页签/子导航里展示</summary>
        public bool IsVisible { get; set; }

        /// <summary>路由配置 JSON，保持入库原文</summary>
        public string? RouteConfig { get; set; }

        /// <summary>备注（仅后台使用）</summary>
        public string? Remark { get; set; }

        /// <summary>启用或禁用</summary>
        public EmStatus Status { get; set; }

        /// <summary>从实体映射。</summary>
        public static MenuSubPageAdmin From(AtMenuSubPage page) => new()
        {
            Id = page.Id,
            MenuId = page.MenuId,
            Title = page.Title ?? string.Empty,
            Sort = page.Sort,
            IsVisible = page.IsVisible,
            RouteConfig = page.RouteConfig,
            Remark = page.Remark,
            Status = page.Status
        };
    }
}
