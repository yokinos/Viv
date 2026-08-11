namespace Viv.Engine.Tests;

/// <summary>
/// VivRunDefine —— 上下文头契约与状态码白名单的集中存放处，网关/下游/结果层跨层共用。
/// 任何一处改值都会影响签名协议与 HTTP 行为，是跨层静态锚点。
/// </summary>
public class VivRunDefineTests
{
    [Fact]
    public void 上下文头契约()
    {
        Assert.Equal("x-viv-appId", VivRunDefine.AppIdHeader);
        Assert.Equal("x-viv-subjectId", VivRunDefine.SubjectIdHeader);
        Assert.Equal("x-viv-userId", VivRunDefine.UserIdHeader);
        Assert.Equal("x-viv-serviceName", VivRunDefine.ServiceNameHeader);
        Assert.Equal("x-request-token", VivRunDefine.InnerRequestTokenHeader);
    }

    [Fact]
    public void 状态码白名单()
    {
        var allowed = VivRunDefine.AllowedHttpStatusCodes;
        foreach (var code in new[] { 301, 302, 303, 307, 308, 304, 401, 403, 404, 405, 406, 415 })
        {
            Assert.Contains(code, allowed);
        }
        Assert.DoesNotContain(200, allowed);
        Assert.DoesNotContain(400, allowed);
        Assert.DoesNotContain(500, allowed);
    }
}
