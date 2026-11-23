using Viv.Contracts.Interface;
using Viv.Keke.Identity;

namespace Viv.Keke.Tests;

/// <summary>
/// 身份头值快照 —— gRPC 拦截器与 MagicOnion Filter 共用这一份产物。
/// 键必须小写（gRPC metadata 写盘要求），可选头为空时整条不带（空串会让下游误判成「上游给了个空值」）。
/// </summary>
public class VivIdentityValuesTests
{
    private static VivIdentityValues Sample(string? traceId = null, string? requestToken = null)
        => new("1", "2", "3", "viv.apex.api", "holder-1", traceId, requestToken);

    private static List<KeyValuePair<string, string>> Pairs(string? traceId = null, string? requestToken = null)
        => Sample(traceId, requestToken).ToHeaderPairs().ToList();

    [Fact]
    public void 键全部小写()
    {
        var pairs = Pairs(traceId: "trace-1", requestToken: "1:abc");

        Assert.All(pairs, p => Assert.Equal(p.Key, p.Key.ToLowerInvariant()));
    }

    [Fact]
    public void 五个身份头始终带齐()
    {
        var keys = Pairs().Select(p => p.Key).ToList();

        Assert.Contains(VivIdentityHeaders.Key(VivHeaderContract.AppId), keys);
        Assert.Contains(VivIdentityHeaders.Key(VivHeaderContract.SubjectId), keys);
        Assert.Contains(VivIdentityHeaders.Key(VivHeaderContract.UserId), keys);
        Assert.Contains(VivIdentityHeaders.Key(VivHeaderContract.ServiceName), keys);
        Assert.Contains(VivIdentityHeaders.Key(VivHeaderContract.HolderId), keys);
    }

    [Fact]
    public void traceId为空时整条不带()
    {
        Assert.DoesNotContain(VivIdentityHeaders.Key(VivHeaderContract.TraceId), Pairs().Select(p => p.Key));
        Assert.DoesNotContain(VivIdentityHeaders.Key(VivHeaderContract.TraceId), Pairs(traceId: "").Select(p => p.Key));
        Assert.DoesNotContain(VivIdentityHeaders.Key(VivHeaderContract.TraceId), Pairs(traceId: "   ").Select(p => p.Key));
    }

    [Fact]
    public void traceId有值时带上()
    {
        var pairs = Pairs(traceId: "trace-1");

        Assert.Equal("trace-1", pairs.Single(p => p.Key == VivIdentityHeaders.Key(VivHeaderContract.TraceId)).Value);
    }

    [Fact]
    public void requestToken为空时整条不带()
    {
        Assert.DoesNotContain(VivIdentityHeaders.Key(VivHeaderContract.InnerRequestToken), Pairs().Select(p => p.Key));
    }

    [Fact]
    public void requestToken有值时带上()
    {
        var pairs = Pairs(requestToken: "1:abc");

        Assert.Equal("1:abc", pairs.Single(p => p.Key == VivIdentityHeaders.Key(VivHeaderContract.InnerRequestToken)).Value);
    }
}
