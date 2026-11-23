using Grpc.Core;
using Viv.Keke.Identity;
using Viv.Keke.MagicOnion.Filters;

namespace Viv.Keke.Tests;

/// <summary>
/// MagicOnion 客户端 Filter 的写头逻辑 —— 与 gRPC <c>VivGrpcInterceptor</c> 的唯一语义差别就在这里：
/// <b>覆盖</b>而非「缺了才加」。
///
/// <para>
/// 为什么不直接调 <c>SendAsync</c>：它的入参 <c>RequestContext</c> 是 MagicOnion 的抽象类，
/// 没有公开实现可构造。写头这段纯逻辑抽成 internal 后行为完全一样，测它等价。
/// </para>
/// </summary>
public class VivIdentityClientFilterTests
{
    [Fact]
    public void 写入全部身份头()
    {
        var headers = new Metadata();

        VivIdentityClientFilter.WriteHeaders(headers, Values());

        Assert.Equal("11", headers.Get("x-viv-appid")?.Value);
        Assert.Equal("22", headers.Get("x-viv-subjectid")?.Value);
        Assert.Equal("33", headers.Get("x-viv-userid")?.Value);
        Assert.Equal("viv.apex.api", headers.Get("x-viv-servicename")?.Value);
        Assert.Equal("holder-1", headers.Get("x-viv-holder-id")?.Value);
        Assert.Equal("trace-1", headers.Get("x-trace-id")?.Value);
        Assert.Equal("1:sig", headers.Get("x-request-token")?.Value);
    }

    [Fact]
    public void traceId与token为空时整条不带()
    {
        var headers = new Metadata();

        VivIdentityClientFilter.WriteHeaders(headers, Values(traceId: null, requestToken: null));

        // 塞空串会让下游误判成「上游给了个空值」，而不是「上游没给」
        Assert.Null(headers.Get("x-trace-id"));
        Assert.Null(headers.Get("x-request-token"));
    }

    [Fact]
    public void 已存在的同名头被覆盖而不是追加()
    {
        var headers = new Metadata
        {
            { "x-viv-holder-id", "stale-holder" },
            { "x-request-token", "999:stale-signature" },
        };

        VivIdentityClientFilter.WriteHeaders(headers, Values());

        // 复用同一个客户端实例时旧值/旧签名不能累积：一次请求带多个 holderId 时，
        // 服务端 Metadata.Get 取最前面那个、验签却按最后写入的算
        Assert.Single(headers.Where(e => e.Key == "x-viv-holder-id"));
        Assert.Equal("holder-1", headers.Get("x-viv-holder-id")?.Value);

        Assert.Single(headers.Where(e => e.Key == "x-request-token"));
        Assert.Equal("1:sig", headers.Get("x-request-token")?.Value);
    }

    [Fact]
    public void 连续调用两次不会累积()
    {
        var headers = new Metadata();

        VivIdentityClientFilter.WriteHeaders(headers, Values(requestToken: "1:first"));
        VivIdentityClientFilter.WriteHeaders(headers, Values(requestToken: "2:second"));

        Assert.Single(headers.Where(e => e.Key == "x-request-token"));
        Assert.Equal("2:second", headers.Get("x-request-token")?.Value);
    }

    [Fact]
    public void Headers未初始化时抛异常而不是静默跳过()
    {
        // 静默跳过会变成「服务端按未认证拒绝」，把配置问题伪装成鉴权失败
        Assert.Throws<InvalidOperationException>(() => VivIdentityClientFilter.WriteHeaders(null, Values()));
    }

    private static VivIdentityValues Values(string? traceId = "trace-1", string? requestToken = "1:sig")
        => new("11", "22", "33", "viv.apex.api", "holder-1", traceId, requestToken);
}
