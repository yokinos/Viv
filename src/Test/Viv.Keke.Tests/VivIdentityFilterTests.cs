using Grpc.Core;
using Viv.Contracts;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Keke.Identity;
using Viv.Keke.MagicOnion.Filters;

namespace Viv.Keke.Tests;

/// <summary>
/// MagicOnion 服务端 Filter 的验签映射 —— 与 gRPC <c>VivGrpcServerInterceptor</c> 同口径：
/// <see cref="VivIdentityException"/> 一律映射成 <see cref="StatusCode.Unauthenticated"/>，
/// 不区分缺签名 / 坏签名 / 过期。
///
/// <para>
/// 不直接调 <c>Invoke</c>：它的入参 <c>ServiceContext</c> 构造需要 CallInvoker、MethodHandler 等一整套，
/// 而 Invoke 里除了这次验签映射，剩下的只是写 DI 上下文与放行。测 <c>ValidateOrThrow</c> 覆盖了真正的判断。
/// </para>
/// </summary>
[Collection(KekeStaticStateCollection.Name)]
public class VivIdentityFilterTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    private static readonly VivContextContent DefaultSnapshot = new()
    {
        AppId = 11,
        SubjectId = 22,
        UserId = 33,
    };

    public VivIdentityFilterTests() => VivCallContext.Clear();

    [Fact]
    public void 验签通过_返回上下文()
    {
        var headers = BuildHeaders();

        var result = VivIdentityFilter.ValidateOrThrow(Reader(headers), Options());

        Assert.NotNull(result.Context);
        Assert.Equal(11, result.Context!.AppId);
        Assert.Equal(22, result.Context.SubjectId);
        Assert.Equal(33, result.Context.UserId);
    }

    [Fact]
    public void 篡改holderId_映射成Unauthenticated()
    {
        var headers = BuildHeaders();
        headers["x-viv-holder-id"] = "forged-holder";

        var ex = Assert.Throws<RpcException>(() => VivIdentityFilter.ValidateOrThrow(Reader(headers), Options()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public void 缺签名token_映射成Unauthenticated()
    {
        var headers = BuildHeaders();
        headers.Remove("x-request-token");

        var ex = Assert.Throws<RpcException>(() => VivIdentityFilter.ValidateOrThrow(Reader(headers), Options()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public void 未配密钥_返回空上下文且不抛()
    {
        var headers = BuildHeaders(secret: null);

        // 本地开发：没配密钥就不验签、不灌上下文，但不该拒绝调用
        var result = VivIdentityFilter.ValidateOrThrow(Reader(headers), Options(secret: null));

        Assert.Null(result.Context);
    }

    [Fact]
    public void AppId非正_返回空上下文且不抛()
    {
        var headers = BuildHeaders(new VivContextContent { AppId = 0, SubjectId = 22, UserId = 33 });

        var result = VivIdentityFilter.ValidateOrThrow(Reader(headers), Options());

        Assert.Null(result.Context);
    }

    private static VivInternalTokenOptions Options(string? secret = Secret)
        => new() { InternalToken = secret, ServiceName = "viv.apex.api" };

    private static Dictionary<string, string?> BuildHeaders(VivContextContent? snapshot = null, string? secret = Secret)
    {
        var values = VivIdentityHeaders.Build(snapshot ?? DefaultSnapshot, Options(secret));

        return values.ToHeaderPairs()
            .ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Func<string, string?> Reader(Dictionary<string, string?> headers)
        => key => headers.TryGetValue(key, out var value) ? value : null;
}
