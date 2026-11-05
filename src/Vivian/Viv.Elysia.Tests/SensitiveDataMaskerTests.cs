using Viv.Elysia.Security;

namespace Viv.Elysia.Tests;

/// <summary>
/// 操作日志请求体脱敏：口令 / 令牌 / 密钥类字段必须打码，其余字段原样保留。
/// </summary>
public class SensitiveDataMaskerTests
{
    [Fact]
    public void 登录请求体_口令被打码_账号保留()
    {
        var masked = SensitiveDataMasker.Mask(
            """{"userName":"13800000000","password":"P@ssw0rd!","userType":1}""");

        Assert.DoesNotContain("P@ssw0rd!", masked);
        Assert.Contains("***", masked);
        Assert.Contains("13800000000", masked);
    }

    [Fact]
    public void 嵌套字段与改名写法都能命中()
    {
        var masked = SensitiveDataMasker.Mask(
            """{"user":{"New_Password":"a1","old-password":"b2","name":"n"}}""");

        Assert.DoesNotContain("a1", masked);
        Assert.DoesNotContain("b2", masked);
        Assert.Contains("n", masked);
    }

    [Fact]
    public void 令牌字段被打码()
    {
        var masked = SensitiveDataMasker.Mask(
            """{"refreshToken":"r-123","access_token":"a-456"}""");

        Assert.DoesNotContain("r-123", masked);
        Assert.DoesNotContain("a-456", masked);
    }

    [Fact]
    public void 数组内的敏感字段同样处理()
    {
        var masked = SensitiveDataMasker.Mask("""[{"name":"a","secret":"s1"},{"name":"b","secret":"s2"}]""");

        Assert.DoesNotContain("s1", masked);
        Assert.DoesNotContain("s2", masked);
        Assert.Contains("\"a\"", masked);
    }

    [Fact]
    public void 表单请求体按字段名打码()
    {
        var masked = SensitiveDataMasker.Mask("userName=bob&password=secret-value&remember=true");

        Assert.DoesNotContain("secret-value", masked);
        Assert.Contains("userName=bob", masked);
        Assert.Contains("remember=true", masked);
    }

    [Fact]
    public void 无敏感字段的JSON原样保留()
    {
        const string body = """{"page":1,"keyword":"abc"}""";
        Assert.Equal(body, SensitiveDataMasker.Mask(body));
    }

    [Fact]
    public void 空体返回原值()
    {
        Assert.Null(SensitiveDataMasker.Mask(null));
        Assert.Equal(string.Empty, SensitiveDataMasker.Mask(string.Empty));
    }

    [Fact]
    public void 声明为JSON但解析失败_整段打码()
    {
        // 结构坏了也不能把内容当明文发出去
        Assert.Equal("***", SensitiveDataMasker.Mask("""{"password": "oops"""));
    }
}
