using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>给指定应用下的页面菜单新增或更新动作（权限点）。Id 为 0 是新增。</summary>
    public class SaveAppMenuActionRequest : VivApiRequest
    {
        /// <summary>菜单所属的客户端应用 Id</summary>
        public long ClientAppId { get; set; }

        /// <summary>动作 Id，0 = 新增</summary>
        public long Id { get; set; }

        /// <summary>动作所属的页面菜单 Id</summary>
        public long MenuId { get; set; }

        /// <summary>动作名称</summary>
        [Required]
        [StringLength(100)]
        [DisplayName("动作名称")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 动作编码（如 <c>user:create</c>），服务端 <c>[VivRequirePermission]</c> 按它判定。
        /// 全局唯一（跨应用重名请自己加前缀）。留空 = 这个权限点只能做前端按钮显隐，服务端查不到它。
        /// </summary>
        [StringLength(128)]
        [DisplayName("动作编码")]
        public string? ActionCode { get; set; }

        /// <summary>排序号，越小越靠前</summary>
        public int Sort { get; set; }

        /// <summary>备注（仅后台使用）</summary>
        [StringLength(500)]
        [DisplayName("备注")]
        public string? Remark { get; set; }

        /// <summary>启用 / 禁用</summary>
        public EmStatus Status { get; set; }
    }
}
