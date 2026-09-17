using Microsoft.Extensions.DependencyInjection;
using Viv.Fakes;
using Viv.Momo;
using Viv.Momo.Options;

namespace Viv.Engine.Tests;

/// <summary>
/// <see cref="VivStartupSchemaSync"/> 的开关语义。同步本身要真库，不在覆盖范围内。
/// </summary>
public class StartupSchemaSyncTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions? options, IMomoDbContext? db)
    {
        var services = new ServiceCollection();
        if (options is not null) services.AddSingleton(options);
        if (db is not null) services.AddScoped(_ => db);
        return services.BuildServiceProvider();
    }

    private static (IMomoDbContext Db, TestProxy Proxy) StubDb(bool throws = false)
    {
        TestProxy? proxy = null;
        var db = TestProxy.Create<IMomoDbContext>(p =>
        {
            proxy = p;
            p.ThrowOnAnyCall = throws;
        });
        return (db, proxy!);
    }

    [Fact]
    public void 开关默认关_不碰数据库()
    {
        var (db, proxy) = StubDb();
        using var provider = BuildProvider(new DatabaseOptions(), db);

        VivStartupSchemaSync.Run(provider);

        Assert.DoesNotContain("SyncTableAsync", proxy.Calls);
    }

    [Fact]
    public void 开关打开_同步一次()
    {
        var (db, proxy) = StubDb();
        using var provider = BuildProvider(new DatabaseOptions { SyncTableOnStartup = true }, db);

        VivStartupSchemaSync.Run(provider);

        Assert.Single(proxy.Calls, c => c == "SyncTableAsync");
    }

    [Fact]
    public void 没配数据库_直接返回不抛()
    {
        // DatabaseOption 为 null（无库的服务），等同于开关关闭
        using var provider = BuildProvider(null, null);

        VivStartupSchemaSync.Run(provider);
    }

    [Fact]
    public void 同步或作用域释放失败_都不冒泡到启动()
    {
        // 替身连 Dispose 一起抛，覆盖「作用域释放失败」这条 ——
        // 它原先在 try 之外，会逃出去把启动拽下来
        var (db, proxy) = StubDb(throws: true);
        using var provider = BuildProvider(new DatabaseOptions { SyncTableOnStartup = true }, db);

        VivStartupSchemaSync.Run(provider);

        Assert.Contains("SyncTableAsync", proxy.Calls);
    }
}
