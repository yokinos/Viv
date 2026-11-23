using Viv.Contracts;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Keke.Identity;

namespace Viv.Keke.Tests;

/// <summary>身份头构造 —— 传输出发点。</summary>
[Collection(KekeStaticStateCollection.Name)]
public class VivIdentityHeadersTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    private static readonly VivContextContent Snapshot = new()
    {
        AppId = 11,
        SubjectId = 22,
        UserId = 33,
        HolderId = "snapshot-holder",
    };

    public VivIdentityHeadersTests() => VivCallContext.Clear();

    [Fact]
    public void 未配密钥_不签requestToken()
    {
        var values = VivIdentityHeaders.Build(Snapshot, new VivInternalTokenOptions { ServiceName = "viv.apex.api" });

        Assert.Null(values.RequestToken);
    }

    [Fact]
    public void 配了密钥_签出的token能通过验签()
    {
        var values = VivIdentityHeaders.Build(
            Snapshot,
            new VivInternalTokenOptions { InternalToken = Secret, ServiceName = "viv.apex.api" });

        // 端到端自洽：签名载荷一旦在两侧漂移，服务端只会静默拒绝，而两边单测各自都还是绿的
        Assert.NotNull(values.RequestToken);
        Assert.True(VivRequestToken.TryVerify(
            values.RequestToken, values.AppId, values.SubjectId, values.UserId, values.ServiceName, values.HolderId, Secret));
    }

    [Fact]
    public void 身份字段取自快照_serviceName取自配置()
    {
        var values = VivIdentityHeaders.Build(
            Snapshot,
            new VivInternalTokenOptions { ServiceName = "viv.apex.api" });

        Assert.Equal("11", values.AppId);
        Assert.Equal("22", values.SubjectId);
        Assert.Equal("33", values.UserId);
        Assert.Equal("viv.apex.api", values.ServiceName);
    }

    [Fact]
    public void traceId跟随VivCallContext()
    {
        VivCallContext.SetTraceId("trace-1");

        var values = VivIdentityHeaders.Build(Snapshot, new VivInternalTokenOptions());

        Assert.Equal("trace-1", values.TraceId);
    }

    [Fact]
    public void holderId跟随VivCallContext而非快照()
    {
        VivCallContext.SetHolderId("live-holder");

        var values = VivIdentityHeaders.Build(Snapshot, new VivInternalTokenOptions());

        // 快照里的 HolderId 是「上一次调用看到的」，真正该带出去的是当前这一跳的持有者
        Assert.Equal("live-holder", values.HolderId);
        Assert.NotEqual(Snapshot.HolderId, values.HolderId);
    }

    [Fact]
    public void 快照为空时身份字段回落0()
    {
        var values = VivIdentityHeaders.Build((VivContextContent?)null, new VivInternalTokenOptions());

        Assert.Equal("0", values.AppId);
        Assert.Equal("0", values.SubjectId);
        Assert.Equal("0", values.UserId);
        Assert.Equal(string.Empty, values.ServiceName);
    }
}
