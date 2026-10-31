using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>某个客户端应用的菜单维护视图。不含密钥。</summary>
    public class AppMenuTreeOutput
    {
        /// <summary>客户端应用 Id。</summary>
        public long ClientAppId { get; set; }

        /// <summary>应用名称。</summary>
        public string? Name { get; set; }

        /// <summary>客户端对外标识。</summary>
        public string? AppKey { get; set; }

        /// <summary>前端技术栈。只有 Vue 才允许在这个页面改菜单。</summary>
        public EmClientStack ClientStack { get; set; }

        /// <summary>菜单树。子页面挂在所属页面后面，IsSubPage 为 true。</summary>
        public List<MenuAdminNode> Menus { get; set; } = [];
    }
}
