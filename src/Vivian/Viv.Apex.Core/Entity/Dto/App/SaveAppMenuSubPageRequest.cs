using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>给指定应用下的页面菜单新增或更新子页面。Id 为 0 是新增。</summary>
    public class SaveAppMenuSubPageRequest : VivApiRequest
    {
        /// <summary>菜单所属的客户端应用 Id</summary>
        public long ClientAppId { get; set; }

        /// <summary>子页面 Id，0 = 新增</summary>
        public long Id { get; set; }

        /// <summary>子页面所属的页面菜单 Id</summary>
        public long MenuId { get; set; }

        /// <summary>子页面名称</summary>
        [Required]
        [StringLength(100)]
        [DisplayName("子页面名称")]
        public string Title { get; set; } = string.Empty;

        /// <summary>路由配置 JSON，由当前客户端技术栈的适配器解释</summary>
        public string? RouteConfig { get; set; }

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>是否在页签/子导航里展示</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>备注（仅后台使用）</summary>
        [StringLength(500)]
        [DisplayName("备注")]
        public string? Remark { get; set; }

        /// <summary>启用 / 禁用</summary>
        public EmStatus Status { get; set; }
    }
}
