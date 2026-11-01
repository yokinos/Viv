using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>上级菜单下拉的候选项。只带组树需要的最小字段。</summary>
    public class MenuOption
    {
        /// <summary>菜单 Id</summary>
        public long Id { get; set; }

        /// <summary>父菜单 Id，顶级为 0</summary>
        public long ParentId { get; set; }

        /// <summary>菜单名称</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>目录或页面</summary>
        public EmMenuType Type { get; set; }
    }
}
