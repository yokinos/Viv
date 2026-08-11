using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Engine.Power;

namespace Viv.Engine.Tests;

/// <summary>
/// RequestTokenResolver —— 网关与下游间的 x-request-token 共享密钥签名协议（P0 安全路径）。
/// 纯逻辑：HMAC-SHA256 签名 4 个 x-viv-* 头 + unix 时间戳，下游在 300s 防重放窗口内验签。
/// 密钥优先级 EnvOption.InternalToken &gt; TokenOptions.SecretKey（回落）。全部测试收在一个类里，
/// 因为 VivEngine.VivOptions / VivConfigRegistry 是静态共享状态，类内顺序执行可避免跨类并行污染。
/// </summary>
public class RequestTokenResolverTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    public RequestTokenResolverTests()
    {
        EngineTestEnv.ForceFallbackMode();
        VivConfigRegistry.Remove<TokenOptions>();
    }

    #region 签名 / 验签

    [Fact]
    public void SignContextHeaders_无密钥返回null()
    {
        Assert.Null(RequestTokenResolver.SignContextHeaders(new HeaderDictionary()));
    }

    [Fact]
    public void SignContextHeaders_有密钥返回冒号分隔签名()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = Secret });
        var headers = ContextHeaders();

        var token = RequestTokenResolver.SignContextHeaders(headers);

        Assert.NotNull(token);
        var sep = token!.IndexOf(':');
        Assert.True(sep > 0);
        Assert.True(long.TryParse(token.AsSpan(0, sep), out _));
    }

    [Fact]
    public void 签名验证_回环成功()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = Secret });
        var headers = ContextHeaders();
        headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);

        Assert.True(RequestTokenResolver.VerifySignature(headers, Secret));
    }

    [Fact]
    public void 签名验证_篡改头失败()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = Secret });
        var headers = ContextHeaders();
        headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);
        headers[VivRunDefine.UserIdHeader] = "999"; // 篡改

        Assert.False(RequestTokenResolver.VerifySignature(headers, Secret));
    }

    [Fact]
    public void 签名验证_无token失败()
    {
        Assert.False(RequestTokenResolver.VerifySignature(ContextHeaders(), Secret));
    }

    [Fact]
    public void 签名验证_旧格式无冒号拒绝()
    {
        var headers = ContextHeaders();
        headers[VivRunDefine.InnerRequestTokenHeader] = "just-a-base64-sig";

        Assert.False(RequestTokenResolver.VerifySignature(headers, Secret));
    }

    [Fact]
    public void 签名验证_超过重放窗口拒绝()
    {
        var headers = ContextHeaders();
        long oldTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 301;
        headers[VivRunDefine.InnerRequestTokenHeader] = oldTs + ":" + ComputeSignature(headers, Secret, oldTs);

        Assert.False(RequestTokenResolver.VerifySignature(headers, Secret));
    }

    [Fact]
    public void 签名验证_窗口内通过()
    {
        var headers = ContextHeaders();
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 299;
        headers[VivRunDefine.InnerRequestTokenHeader] = ts + ":" + ComputeSignature(headers, Secret, ts);

        Assert.True(RequestTokenResolver.VerifySignature(headers, Secret));
    }

    [Fact]
    public void 签名验证_密钥不匹配失败()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = "secret-A" });
        var headers = ContextHeaders();
        headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);

        Assert.False(RequestTokenResolver.VerifySignature(headers, "secret-B"));
    }

    #endregion

    #region GetContextFromHeaders

    [Fact]
    public void GetContextFromHeaders_无密钥信任头()
    {
        var result = RequestTokenResolver.GetContextFromHeaders(HttpContextFrom(ContextHeaders()));

        Assert.NotNull(result);
        Assert.Equal(1, result!.AppId);
        Assert.Equal(3, result.SubjectId);
        Assert.Equal(2, result.UserId);
    }

    [Fact]
    public void GetContextFromHeaders_有密钥未签名返回null()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = Secret });

        Assert.Null(RequestTokenResolver.GetContextFromHeaders(HttpContextFrom(ContextHeaders())));
    }

    [Fact]
    public void GetContextFromHeaders_有密钥签名后解析()
    {
        VivConfigRegistry.Add(new TokenOptions { SecretKey = Secret });
        var headers = ContextHeaders();
        headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);

        var result = RequestTokenResolver.GetContextFromHeaders(HttpContextFrom(headers));

        Assert.NotNull(result);
        Assert.Equal(1, result!.AppId);
        Assert.Equal(3, result.SubjectId);
        Assert.Equal(2, result.UserId);
    }

    [Fact]
    public void GetContextFromHeaders_非法appId返回null()
    {
        var headers = ContextHeaders();
        headers[VivRunDefine.AppIdHeader] = "abc";

        Assert.Null(RequestTokenResolver.GetContextFromHeaders(HttpContextFrom(headers)));
    }

    [Fact]
    public void GetContextFromHeaders_缺userId返回null()
    {
        var headers = ContextHeaders();
        headers.Remove(VivRunDefine.UserIdHeader);

        Assert.Null(RequestTokenResolver.GetContextFromHeaders(HttpContextFrom(headers)));
    }

    #endregion

    #region GetContextFromTokenAsync

    [Fact]
    public async Task GetContextFromTokenAsync_有效claims解析()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "2"),
            new Claim(VivClaimTypes.AppId, "1"),
            new Claim(VivClaimTypes.TenantId, "3"),
        }, "test"));

        var result = await RequestTokenResolver.GetContextFromTokenAsync(ctx);

        Assert.NotNull(result);
        Assert.Equal(1, result!.AppId);
        Assert.Equal(3, result.SubjectId);
        Assert.Equal(2, result.UserId);
    }

    [Fact]
    public async Task GetContextFromTokenAsync_未认证返回null()
    {
        var ctx = new DefaultHttpContext(); // 默认 User 未认证

        Assert.Null(await RequestTokenResolver.GetContextFromTokenAsync(ctx));
    }

    [Fact]
    public async Task GetContextFromTokenAsync_缺AppId返回null()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "2"),
        }, "test"));

        Assert.Null(await RequestTokenResolver.GetContextFromTokenAsync(ctx));
    }

    #endregion

    #region EnvOption.InternalToken 优先级

    [Fact]
    public void InternalToken优先于TokenOptions回落()
    {
        try
        {
            // 故意放一个冲突的回落密钥
            VivConfigRegistry.Add(new TokenOptions { SecretKey = "fallback-secret" });
            EngineTestEnv.ForceEnvTokenMode("env-secret");

            var headers = ContextHeaders();
            headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);

            // 用 EnvOption.InternalToken 验签通过 → 证明签名用的是 EnvOption
            Assert.True(RequestTokenResolver.VerifySignature(headers, "env-secret"));
            // 回落密钥验签失败
            Assert.False(RequestTokenResolver.VerifySignature(headers, "fallback-secret"));
        }
        finally
        {
            EngineTestEnv.ForceFallbackMode();
            VivConfigRegistry.Remove<TokenOptions>();
        }
    }

    #endregion

    #region 辅助

    private static IHeaderDictionary ContextHeaders()
    {
        return new HeaderDictionary
        {
            [VivRunDefine.AppIdHeader] = "1",
            [VivRunDefine.SubjectIdHeader] = "3",
            [VivRunDefine.UserIdHeader] = "2",
            [VivRunDefine.ServiceNameHeader] = "viv.apex.api",
        };
    }

    private static DefaultHttpContext HttpContextFrom(IHeaderDictionary headers)
    {
        var ctx = new DefaultHttpContext();
        foreach (var (key, value) in headers)
        {
            ctx.Request.Headers[key] = value;
        }
        return ctx;
    }

    /// <summary>复刻框架 ComputeSignature（私有），伪造任意时间戳的 token，验证防重放窗口。</summary>
    private static string ComputeSignature(IHeaderDictionary headers, string secret, long timestamp)
    {
        var payload = string.Join('\n',
            headers[VivRunDefine.AppIdHeader].ToString(),
            headers[VivRunDefine.SubjectIdHeader].ToString(),
            headers[VivRunDefine.UserIdHeader].ToString(),
            headers[VivRunDefine.ServiceNameHeader].ToString(),
            timestamp.ToString(CultureInfo.InvariantCulture));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    #endregion
}
