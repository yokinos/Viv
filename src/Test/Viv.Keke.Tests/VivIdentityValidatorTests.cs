using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Keke.Identity;

namespace Viv.Keke.Tests;

/// <summary>
/// 身份头验签与还原 —— Keke 的安全边界。
///
/// 三条分支的语义必须分清：
/// <list type="bullet">
/// <item><description>未配密钥：不验签、不灌上下文，<b>不抛</b>（本地开发）。</description></item>
/// <item><description>签名错 / 过期 / 缺签名：<b>抛</b> <see cref="VivIdentityException"/>，调用方映射成 Unauthenticated。</description></item>
/// <item><description>签名对但 AppId 非正：返回空上下文，<b>不抛</b>（签名有效，只是没有租户）。</description></item>
/// </list>
/// </summary>
[Collection(KekeStaticStateCollection.Name)]
public class VivIdentityValidatorTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    private static readonly VivContextContent DefaultSnapshot = new()
    {
        AppId = 11,
        SubjectId = 22,
        UserId = 33,
    };

    public VivIdentityValidatorTests() => VivCallContext.Clear();

    [Fact]
    public void 未配密钥_返回空上下文且不抛()
    {
        var headers = BuildHeaders(secret: null);

        var result = VivIdentityValidator.Validate(Reader(headers), Options(secret: null));

        // 本地开发方便：没配密钥就不验签，也不灌上下文
        Assert.Null(result.Context);
        Assert.Null(result.HolderId);
    }

    [Fact]
    public void 验签通过_还原上下文()
    {
        var headers = BuildHeaders();

        var result = VivIdentityValidator.Validate(Reader(headers), Options());

        Assert.NotNull(result.Context);
        Assert.Equal(11, result.Context!.AppId);
        Assert.Equal(22, result.Context.SubjectId);
        Assert.Equal(33, result.Context.UserId);
        Assert.False(string.IsNullOrWhiteSpace(result.HolderId));
    }

    [Fact]
    public void 篡改holderId_抛身份异常()
    {
        var headers = BuildHeaders();
        headers[VivHeaderContract.HolderId] = "forged-holder";

        // 头被改过但签名还是原来的 → 验签必然失败
        Assert.Throws<VivIdentityException>(() => VivIdentityValidator.Validate(Reader(headers), Options()));
    }

    [Fact]
    public void 缺签名token_抛身份异常()
    {
        var headers = BuildHeaders();
        headers.Remove(VivHeaderContract.InnerRequestToken);

        Assert.Throws<VivIdentityException>(() => VivIdentityValidator.Validate(Reader(headers), Options()));
    }

    [Fact]
    public void 换了密钥_抛身份异常()
    {
        var headers = BuildHeaders();

        Assert.Throws<VivIdentityException>(() => VivIdentityValidator.Validate(Reader(headers), Options("another-secret")));
    }

    [Fact]
    public void AppId非正_返回空上下文但不抛()
    {
        var headers = BuildHeaders(new VivContextContent { AppId = 0, SubjectId = 22, UserId = 33 });

        var result = VivIdentityValidator.Validate(Reader(headers), Options());

        // 签名是对的，只是内容表明「没有可用租户」—— 调用该继续，上下文留空
        Assert.Null(result.Context);
    }

    private static VivInternalTokenOptions Options(string? secret = Secret)
        => new() { InternalToken = secret, ServiceName = "viv.apex.api" };

    /// <summary>
    /// 用真实的 <see cref="VivIdentityHeaders"/> 造头，而不是手拼 —— 签名载荷一旦在收发两侧漂移，
    /// 手拼的测试会跟着漂移一起变绿，只有端到端自洽才守得住。
    /// </summary>
    private static Dictionary<string, string?> BuildHeaders(VivContextContent? snapshot = null, string? secret = Secret)
    {
        var values = VivIdentityHeaders.Build(snapshot ?? DefaultSnapshot, Options(secret));

        // 键来自 ToHeaderPairs（全小写），读取方用的是 VivHeaderContract（混合大小写），
        // 大小写不敏感比较才等价于 gRPC Metadata.Get 的实际行为
        return values.ToHeaderPairs()
            .ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Func<string, string?> Reader(Dictionary<string, string?> headers)
        => key => headers.TryGetValue(key, out var value) ? value : null;
}
