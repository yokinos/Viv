namespace Viv.ServiceProxy.Apex.Ports
{
    /// <summary>应用分页请求。PageIndex 从 1 开始，PageSize 服务端会收敛到 [1,200]</summary>
    public sealed class AppPageRequest
    {
        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        /// <summary>关键字，匹配名称或 AppKey；可空</summary>
        public string? Keyword { get; set; }
    }

    /// <summary>应用摘要。只带调用方需要的字段，不暴露 Apex 的表结构</summary>
    public sealed record AppItem(long Id, string AppKey, string? Name, bool Enabled);

    /// <summary>应用分页结果</summary>
    public sealed record AppPage(IReadOnlyList<AppItem> Items, long TotalCount, int TotalPages);

    /// <summary>准入校验结果。不允许时 <see cref="Reason"/> 直接写日志</summary>
    public readonly record struct AppCheckResult(bool Allowed, string Reason)
    {
        public static AppCheckResult Ok() => new(true, string.Empty);

        public static AppCheckResult Denied(string reason) => new(false, reason);
    }
}
