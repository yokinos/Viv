using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine;
using Viv.Momo;

namespace Viv.Engine.Tests;

/// <summary>
/// 只测装配这一段：配置有没有绑进来、扫描出的组件在不在注册表里、没启动宿主时设上下文会不会被拒。
///
/// StartAsync 那一段要真起 IHost，而 VivLocator.Initialize 的 _initialized 是永不重置的静态量，
/// 同进程只允许初始化一次 —— 起两次宿主的写法会把用例顺序变成隐式依赖，所以端到端验证交给手工跑 Viv.Toolbox。
/// 仓库既有的启动期测试（StartupSchemaSyncTests / LocalEventFlushHostTests）也是这个口径。
/// </summary>
[Collection("VivEngineStaticState")]
public sealed class VivAppBuilderTests
{
    private static readonly Dictionary<string, string?> BaseConfig = new()
    {
        ["VivOptions:EnvOption:InternalToken"] = "0123456789abcdef0123456789abcdef",
        ["VivOptions:EnvOption:Env"] = "0",
        ["VivOptions:EnvOption:ServiceName"] = "viv.toolbox",
        ["VivOptions:LogOption:LogType"] = "0"
    };

    private static readonly Dictionary<string, string?> DatabaseConfig = new(BaseConfig)
    {
        ["VivOptions:DatabaseOption:DatabaseSource"] = "0",
        ["VivOptions:DatabaseOption:MasterConnectionString"] = "Data Source=:memory:"
    };

    [Fact]
    public void 配了数据库就注册数据库上下文与本地事件总线()
    {
        var app = CreateApp(DatabaseConfig);

        app.AddVivApp();

        // Options 就是绑定产物本身，读得出配置值说明这条链走通了
        Assert.Equal("viv.toolbox", app.Options?.EnvOption?.ServiceName);
        Assert.Contains(app.Services, d => d.ServiceType == typeof(IMomoDbContext));
        Assert.Contains(app.Services, d => d.ServiceType == typeof(IVivLocalEventBus));
    }

    [Fact]
    public void 没配数据库就不注册数据库上下文()
    {
        var app = CreateApp(BaseConfig);

        app.AddVivApp();

        Assert.DoesNotContain(app.Services, d => d.ServiceType == typeof(IMomoDbContext));

        // 本地事件总线与数据库配置无关，总是注册
        Assert.Contains(app.Services, d => d.ServiceType == typeof(IVivLocalEventBus));
    }

    [Fact]
    public void 没启动宿主时设置与清空上下文都直接拒绝()
    {
        var app = CreateApp(BaseConfig);

        app.AddVivApp();

        // 容器也没建起来，两者是同一个信号
        Assert.Null(app.Provider);

        Assert.Throws<InvalidOperationException>(
            () => app.SetVivContext(new VivContextContent { AppId = 1, SubjectId = 2, UserId = 3 }));
        Assert.Throws<InvalidOperationException>(() => app.ClearVivContext());
    }

    [Fact]
    public void 没启动宿主时登记Autofac组件只是攒着()
    {
        var app = CreateApp(BaseConfig);

        // 容器到 StartAsync 里 Build 的那一刻才建，那之前登记是合法的（攒着、Build 时回放）
        app.AddVivApp().ConfigureContainer(_ => { });

        Assert.Null(app.Provider);
        Assert.Throws<ArgumentNullException>(() => app.ConfigureContainer(null!));
    }

    /// <summary>
    /// VivAppBuilder 按 ContentRootPath（= 程序目录）加载 appsettings.json，而测试的输出目录里
    /// 本来就有被引用项目带过来的那份 —— 不清掉的话用例会跑在别的服务的配置上，且只在本地能复现。
    /// </summary>
    private static VivAppBuilder CreateApp(Dictionary<string, string?> config)
    {
        var app = VivAppBuilder.Create();
        app.Configuration.Sources.Clear();
        app.Configuration.AddInMemoryCollection(config);
        return app;
    }
}
