using Viv.Apex.Proxy.Ports;

namespace Viv.Fakes;

/// <summary>
/// 客户端应用查询替身：Apex 那边谁会通过由测试摆布，并记录最近一次问的 AppId。
/// 分页部分给定时任务遍历用，数据集由 <see cref="Apps"/> / <see cref="TotalCount"/> 摆布。
/// </summary>
public sealed class StubApexAppService : IProxyApexAppService
{
    /// <summary>允许的 AppId 集合</summary>
    public HashSet<long> Allowed { get; } = new();

    /// <summary>开启后任何 AppId 都放行，省去逐个填</summary>
    public bool AllowAll { get; set; }

    /// <summary>拒绝时给出的原因</summary>
    public string Reason { get; set; } = "客户端不存在";

    public long? LastAppId { get; private set; }

    /// <summary>最近一次「归属校验」传的主体 Id</summary>
    public long? LastSubjectId { get; private set; }

    /// <summary>最近一次「归属校验」传的用户类型（EmUserType 的数值）</summary>
    public int? LastUserType { get; private set; }

    public int Calls { get; private set; }

    /// <summary>只允许该主体通过；为空时不按主体过滤</summary>
    public long? AllowedSubjectId { get; set; }

    /// <summary>分页查询返回的全量数据，按 PageIndex/PageSize 切片</summary>
    public List<AppItem> Apps { get; } = new();

    /// <summary>总数。不设置时取 <see cref="Apps"/> 的条数</summary>
    public int? TotalCount { get; set; }

    public int PageCalls { get; private set; }

    public Task<AppCheckResult> CheckAppAsync(long appId, CancellationToken cancellationToken = default)
    {
        LastAppId = appId;
        Calls++;

        return Task.FromResult(AllowAll || Allowed.Contains(appId)
            ? AppCheckResult.Ok()
            : AppCheckResult.Denied(Reason));
    }

    public Task<AppCheckResult> CheckAsync(long subjectId, long appId, int userType, CancellationToken cancellationToken = default)
    {
        LastAppId = appId;
        LastSubjectId = subjectId;
        LastUserType = userType;
        Calls++;

        if (!AllowAll && !Allowed.Contains(appId))
            return Task.FromResult(AppCheckResult.Denied(Reason));

        if (AllowedSubjectId is { } only && only != subjectId)
            return Task.FromResult(AppCheckResult.Denied("该主体未开通此应用"));

        return Task.FromResult(AppCheckResult.Ok());
    }

    public Task<AppPage> GetAppPageAsync(AppPageRequest request, CancellationToken cancellationToken = default)
    {
        PageCalls++;

        var index = Math.Max(1, request.PageIndex);
        var size = Math.Max(1, request.PageSize);
        var total = TotalCount ?? Apps.Count;
        var totalPages = (int)Math.Ceiling(total / (double)size);
        var items = Apps.Skip((index - 1) * size).Take(size).ToList();

        return Task.FromResult(new AppPage(items, total, totalPages));
    }
}
