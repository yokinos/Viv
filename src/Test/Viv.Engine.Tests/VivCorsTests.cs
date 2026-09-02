using Viv.Engine;

namespace Viv.Engine.Tests;

public class VivCorsTests
{
    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://127.0.0.1:5173", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("not-a-uri", false)]
    public void 本机回环Origin判定(string origin, bool expected)
    {
        Assert.Equal(expected, VivCors.IsLoopbackOrigin(origin));
    }
}
