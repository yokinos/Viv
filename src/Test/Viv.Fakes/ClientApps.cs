using Viv.ServiceProxy.Apex;

namespace Viv.Fakes;

/// <summary>
/// 客户端应用查询替身：Apex 那边谁会通过由测试摆布，并记录最近一次问的 AppId。
/// </summary>
public sealed class StubClientAppQuery : IClientAppQuery
{
    /// <summary>允许的 AppId 集合</summary>
    public HashSet<long> Allowed { get; } = new();

    /// <summary>开启后任何 AppId 都放行，省去逐个填</summary>
    public bool AllowAll { get; set; }

    /// <summary>拒绝时给出的原因</summary>
    public string Reason { get; set; } = "客户端不存在";

    public long? LastAppId { get; private set; }

    public int Calls { get; private set; }

    public Task<ClientAppCheck> ValidateAsync(long appId, CancellationToken cancellationToken = default)
    {
        LastAppId = appId;
        Calls++;

        return Task.FromResult(AllowAll || Allowed.Contains(appId)
            ? ClientAppCheck.Ok()
            : ClientAppCheck.Denied(Reason));
    }
}
