using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Viv.Elysia.Interface;

namespace Viv.Fakes;

/// <summary>
/// <see cref="IHostEnvironment"/> 替身 —— 默认是「开发环境」。
/// <c>VivExceptionFilterAttribute</c> 靠它决定要不要把异常详情带进响应，
/// 所以想验「生产环境不吐细节」就把 <see cref="EnvironmentName"/> 改掉。
/// </summary>
public class StubHost : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "test";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>
/// <see cref="IApiRequest"/> 替身 —— 请求校验的最小载体，<see cref="Validate"/> 原样返回构造时给的结果
/// （空串 = 校验通过）。<c>RequestFilterAttribute</c> 不空即拦，所以这一个旋钮就够摆了。
/// </summary>
public class FakeRequest : IApiRequest
{
    private readonly string _error;

    public FakeRequest(string error) => _error = error;

    public string Validate() => _error;
}
