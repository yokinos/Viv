using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Engine.Options;
using Viv.Momo;
using Viv.Momo.Options;
using Viv.Outbox.Options;
using Viv.Redis;
using Viv.Sandrone.Impl;

namespace Viv.Engine.Tests;

/// <summary>
/// 模块总开关与装配顺序 —— 把「节点为 null = 没配」「IsEnabled = false = 显式关掉」这两种
/// 都不装配的情形钉死，防止判定被简化成 <c>option != null</c>（那会让 IsEnabled 变成死配置）。
///
/// 断言用的是 <see cref="IServiceProviderIsService"/>（只看注册表、不实例化），
/// 所以这组测试零数据库、零 Redis、零外部依赖。
/// </summary>
public class VivModuleGateTests
{
    private static ServiceProvider Build(Action<VivOptions>? configure = null)
    {
        var options = new VivOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();
        VivModules.RegisterAll(services, options);
        return services.BuildServiceProvider();
    }

    private static bool Registered(IServiceProvider provider, Type serviceType)
        => provider.GetService<IServiceProviderIsService>()?.IsService(serviceType) == true;

    [Fact]
    public void All_KeepsAssemblyOrder()
    {
        // 顺序即装配顺序，不是字母序：日志必须最先（后面模块出错时要知道往哪写），
        // Nana 必须排在 Database 之前（Saga DbContext 要先于 UseWolverine 登记）
        var names = VivModules.All.Select(static m => m.Name).ToList();

        Assert.Equal("Log", names[0]);
        Assert.True(names.IndexOf("Nana") < names.IndexOf("Database"),
            "Nana 必须排在 Database 之前：Saga DbContext 要先于 UseWolverine 登记");
        Assert.Equal("OtherServices", names[^1]);
    }

    [Fact]
    public void NoOptions_InstallsNothingOptional()
    {
        using var provider = Build();

        Assert.False(Registered(provider, typeof(IMomoDbContext)));
        Assert.False(Registered(provider, typeof(IRedisService)));
        Assert.False(Registered(provider, typeof(IS3Service)));
        Assert.False(Registered(provider, typeof(IVivInbox)));
    }

    [Fact]
    public void Database_IsEnabledFalse_IsSameAsMissing()
    {
        using var provider = Build(o => o.DatabaseOption = new DatabaseOptions { IsEnabled = false });

        Assert.False(Registered(provider, typeof(IMomoDbContext)));
    }

    [Fact]
    public void Database_IsEnabledTrue_Registers()
    {
        using var provider = Build(o => o.DatabaseOption = new DatabaseOptions());

        Assert.True(Registered(provider, typeof(IMomoDbContext)));
    }

    [Theory]
    [InlineData(null)]   // 节点整个没配
    [InlineData(false)]  // 节点在，但显式关掉
    public void Token_IsAlwaysRegistered_AndFallsBackToNone(bool? isEnabled)
    {
        using var provider = Build(o =>
        {
            if (isEnabled.HasValue) o.TokenOption = new TokenOptions { IsEnabled = isEnabled.Value };
        });

        // 永远注册 ITokenService：不注册的话构造注入它的服务全部解析失败
        Assert.True(Registered(provider, typeof(ITokenService)));

        using var scope = provider.CreateScope();
        Assert.IsType<NoneTokenService>(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }

    [Fact]
    public void Inbox_FollowsDatabase_NotItsOwnOption()
    {
        // 配了 InboxOption 但没有数据库：不装 —— Inbox 的真门是 Database
        using var noDatabase = Build(o => o.InboxOption = new InboxOptions());
        Assert.False(Registered(noDatabase, typeof(IVivInbox)));

        // 数据库打开后，InboxOption 缺席也照样装（消费者想用就能用）
        using var withDatabase = Build(o => o.DatabaseOption = new DatabaseOptions());
        Assert.True(Registered(withDatabase, typeof(IVivInbox)));
    }

    [Fact]
    public void HealthChecks_AreAlwaysInstalled()
    {
        // 网关那种「只有 Env/Log/Token」的宿主也要有 /health 端点。
        // 跳过 AddHealthChecks() 会让 MapHealthChecks 在请求时抛「找不到所需服务」，
        // 所以它是唯一一个不受 IsEnabled 控制的模块。
        using var provider = Build();

        Assert.True(Registered(provider, typeof(HealthCheckService)));
    }

    [Fact]
    public void S3_IsGatedByItsOption()
    {
        using var missing = Build();
        Assert.False(Registered(missing, typeof(IS3Service)));

        using var disabled = Build(o => o.S3Option = new S3Options { IsEnabled = false });
        Assert.False(Registered(disabled, typeof(IS3Service)));

        using var enabled = Build(o => o.S3Option = new S3Options());
        Assert.True(Registered(enabled, typeof(IS3Service)));
    }
}
