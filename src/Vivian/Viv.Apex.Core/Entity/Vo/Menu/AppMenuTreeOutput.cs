using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>某个客户端应用的菜单维护视图。不含密钥。</summary>
    public class AppMenuTreeOutput
    {
        public long ClientAppId { get; set; }

        public string? Name { get; set; }

        public string? AppKey { get; set; }

        public EmClientStack ClientStack { get; set; }

        public List<MenuAdminNode> Menus { get; set; } = [];
    }
}
