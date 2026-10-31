using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.App
{
    /// <summary>应用列表行。密钥只留在库里，这里不出现。</summary>
    public class ClientAppListItem
    {
        /// <summary>应用 Id。</summary>
        public long Id { get; set; }

        /// <summary>客户端对外标识。</summary>
        public string? AppKey { get; set; }

        /// <summary>应用名称。</summary>
        public string? Name { get; set; }

        /// <summary>运行平台。</summary>
        public EmAppPlatform Platform { get; set; }

        /// <summary>前端技术栈。</summary>
        public EmClientStack ClientStack { get; set; }

        /// <summary>应用来源。</summary>
        public EmAppSouce Source { get; set; }

        /// <summary>启用或禁用。Enabled = 0。</summary>
        public EmStatus Status { get; set; }

        /// <summary>Android 包名。</summary>
        public string? PackageName { get; set; }

        /// <summary>iOS Bundle Id。</summary>
        public string? BundleId { get; set; }

        /// <summary>备注。</summary>
        public string? Remark { get; set; }

        public static ClientAppListItem From(AtClientApp app) => new()
        {
            Id = app.Id,
            AppKey = app.AppKey,
            Name = app.Name,
            Platform = app.Platform,
            ClientStack = app.ClientStack,
            Source = app.Source,
            Status = app.Status,
            PackageName = app.PackageName,
            BundleId = app.BundleId,
            Remark = app.Remark
        };
    }
}
