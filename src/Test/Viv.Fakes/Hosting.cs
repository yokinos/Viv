using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

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
