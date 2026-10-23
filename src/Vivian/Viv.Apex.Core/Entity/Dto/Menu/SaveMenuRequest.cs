using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.Menu
{
    /// <summary>新增/更新菜单。Id 为 0 是新增，否则按 Id 更新</summary>
    public class SaveMenuRequest : VivApiRequest
    {
        /// <summary>菜单Id，0 = 新增</summary>
        public long Id { get; set; }

        /// <summary>父节点Id，0 = 顶级</summary>
        public long ParentId { get; set; }

        [Required]
        [DisplayName("菜单名称")]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        /// <summary>目录 / 页面</summary>
        public EmMenuType Type { get; set; }

        /// <summary>图标标识</summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>是否在菜单里展示</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>路由配置 JSON，由适配器按客户端技术栈解释；空表示这个节点没有路由</summary>
        public string? RouteConfig { get; set; }

        /// <summary>备注（仅后台使用）</summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>启用 / 禁用</summary>
        public EmStatus Status { get; set; }
    }
}
