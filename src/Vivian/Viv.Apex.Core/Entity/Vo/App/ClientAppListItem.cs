using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Vo.App
{
    /// <summary>应用列表行。密钥只留在库里，这里不出现。</summary>
    public class ClientAppListItem
    {
        public long Id { get; set; }

        public string? AppKey { get; set; }

        public string? Name { get; set; }

        public EmAppPlatform Platform { get; set; }

        public EmClientStack ClientStack { get; set; }

        public EmAppSouce Source { get; set; }

        public EmStatus Status { get; set; }

        public string? PackageName { get; set; }

        public string? BundleId { get; set; }

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
