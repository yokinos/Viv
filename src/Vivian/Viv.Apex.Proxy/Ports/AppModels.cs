namespace Viv.Apex.Proxy.Ports
{
    /// <summary>分页请求。PageIndex 从 1 开始，PageSize 服务端会压到 1..200</summary>
    public sealed class AppPageRequest
    {
        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        /// <summary>关键字，匹配名称或 AppKey</summary>
        public string? Keyword { get; set; }
    }

    /// <summary>应用摘要，只带调用方用得上的字段</summary>
    public sealed record AppItem(long Id, string AppKey, string? Name, bool Enabled);

    /// <summary>分页结果</summary>
    public sealed record AppPage(IReadOnlyList<AppItem> Items, long TotalCount, int TotalPages);

    /// <summary>准入结果。拒绝时 Reason 直接写日志</summary>
    public readonly record struct AppCheckResult(bool Allowed, string Reason)
    {
        public static AppCheckResult Ok() => new(true, string.Empty);

        public static AppCheckResult Denied(string reason) => new(false, reason);
    }
}
