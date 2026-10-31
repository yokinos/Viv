using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.App
{
    /// <summary>新增或更新客户端应用。Id 为 0 是新增。密钥不由请求带入。</summary>
    public class SaveAppRequest : VivApiRequest
    {
        /// <summary>应用 Id。0 表示新增，否则按 Id 更新。</summary>
        public long Id { get; set; }

        /// <summary>应用名称。</summary>
        [Required]
        [StringLength(100)]
        [DisplayName("应用名称")]
        public string Name { get; set; } = string.Empty;

        /// <summary>客户端对外标识，对应 AtClientApp.AppKey，不是主键。</summary>
        [Required]
        [StringLength(64)]
        [DisplayName("AppKey")]
        public string AppKey { get; set; } = string.Empty;

        /// <summary>运行平台，如 Web、H5。</summary>
        public EmAppPlatform Platform { get; set; }

        /// <summary>前端技术栈。菜单路由按这个解释。</summary>
        public EmClientStack ClientStack { get; set; }

        /// <summary>应用来源，如官方自营、代理商定制。</summary>
        public EmAppSouce Source { get; set; }

        /// <summary>启用或禁用。Enabled = 0。</summary>
        public EmStatus Status { get; set; }

        /// <summary>Android 包名，可空。</summary>
        [StringLength(100)]
        [DisplayName("包名")]
        public string? PackageName { get; set; }

        /// <summary>iOS Bundle Id，可空。</summary>
        [StringLength(100)]
        [DisplayName("Bundle Id")]
        public string? BundleId { get; set; }

        /// <summary>备注，可空。</summary>
        [StringLength(500)]
        [DisplayName("备注")]
        public string? Remark { get; set; }
    }
}
