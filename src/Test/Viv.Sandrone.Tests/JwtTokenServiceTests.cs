using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Sandrone.Impl;

namespace Viv.Sandrone.Tests;

/// <summary>
/// JwtTokenService —— 对称密钥 JWT 签发/验证/解析（安全相关核心路径）。
/// 密钥经 VivConfigRegistry 静态通道注入（同 Engine RequestTokenResolver 模式），
/// 全部测试收在一个类里避免跨类并行污染。TokenOptions 校验：签发/验证参数在构造时
/// 快照，ClockSkew=0 严格校验过期。
/// </summary>
public class JwtTokenServiceTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef"; // ≥32 字节，HS256 强制
    private const string Issuer = "viv-test-issuer";
    private const string Audience = "viv-test-audience";

    private static JwtTokenService CreateService()
    {
        VivConfigRegistry.Remove<TokenOptions>();
        VivConfigRegistry.Add(new TokenOptions
        {
            SecretKey = Secret,
            Issuer = Issuer,
            Audience = Audience,
            ExpireMinutes = 60,
        });
        return new JwtTokenService();
    }

    private static string SignToken(string secret, string issuer, string audience, DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, expires: expires ?? DateTime.UtcNow.AddMinutes(60), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void 无密钥构造抛异常()
    {
        VivConfigRegistry.Remove<TokenOptions>();
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService());
    }

    [Fact]
    public void 签发验证回环()
    {
        var service = CreateService();
        var token = service.GenerateToken(new TokenPayload { UserId = 42, UserName = "viv" });

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(service.ValidateToken(token));
    }

    [Fact]
    public void ParseToken全字段回环()
    {
        var service = CreateService();
        var payload = new TokenPayload
        {
            UserId = 42,
            UserName = "viv",
            AppId = 1,
            SubjectId = 3,
            Roles = { "admin", "user" },
            Extensions = { ["dept"] = "rnd" },
        };

        var parsed = service.ParseToken(service.GenerateToken(payload));

        Assert.Equal(42, parsed!.UserId);
        Assert.Equal("viv", parsed.UserName);
        Assert.Equal(1, parsed.AppId);
        Assert.Equal(3, parsed.SubjectId);
        Assert.Equal(new[] { "admin", "user" }, parsed.Roles);
        Assert.Equal("rnd", parsed.Extensions["dept"]);
    }

    [Fact]
    public void 缺省AppIdTenantId解析为0()
    {
        var service = CreateService();
        var parsed = service.ParseToken(service.GenerateToken(new TokenPayload { UserId = 7, UserName = "x" }));

        Assert.Equal(0, parsed!.AppId);
        Assert.Equal(0, parsed.SubjectId);
    }

    [Fact]
    public void 错误密钥验证失败()
    {
        var service = CreateService();
        var token = SignToken("another-secret-0123456789abcdef0123456789abcdef", Issuer, Audience);

        Assert.False(service.ValidateToken(token));
    }

    [Fact]
    public void 过期Token验证失败()
    {
        var service = CreateService();
        var token = SignToken(Secret, Issuer, Audience, DateTime.UtcNow.AddMinutes(-5));

        Assert.False(service.ValidateToken(token));
    }

    [Fact]
    public void 错误受众验证失败()
    {
        var service = CreateService();
        var token = SignToken(Secret, Issuer, "other-audience");

        Assert.False(service.ValidateToken(token));
    }

    [Fact]
    public void 错误签发方验证失败()
    {
        var service = CreateService();
        var token = SignToken(Secret, "other-issuer", Audience);

        Assert.False(service.ValidateToken(token));
    }

    [Fact]
    public void 篡改Token验证失败()
    {
        var service = CreateService();
        var token = service.GenerateToken(new TokenPayload { UserId = 1, UserName = "a" });
        var tampered = token[..^4] + "AAAA"; // 改动签名尾部

        Assert.False(service.ValidateToken(tampered));
    }

    [Fact]
    public void 空与垃圾Token拒绝()
    {
        var service = CreateService();
        Assert.False(service.ValidateToken(""));
        Assert.False(service.ValidateToken("not-a-jwt"));
        Assert.False(service.ValidateToken(null!));
    }

    [Fact]
    public void ParseToken垃圾Token抛InvalidTokenException()
    {
        var service = CreateService();
        Assert.Throws<Viv.Contracts.Exceptions.InvalidTokenException>(() => service.ParseToken("garbage-token"));
    }

    [Fact]
    public void GetOptions返回构造快照()
    {
        var service = CreateService();
        Assert.Equal(Secret, service.GetOptions().SecretKey);
        Assert.Equal(60, service.GetOptions().ExpireMinutes);
    }
}
