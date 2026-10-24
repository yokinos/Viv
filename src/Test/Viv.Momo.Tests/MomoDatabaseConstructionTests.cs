using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// MomoDatabase 的构造与上下文归属：
/// options 在构造时定死、访问器显式注入、工厂按传入配置建会话
/// （不再有「先按默认库构造、再 SetOptions 覆盖」的半初始化路径）。
/// </summary>
public class MomoDatabaseConstructionTests
{
    private static DatabaseOptions Options(string connectionString = "Host=localhost;Database=viv_test")
        => new()
        {
            DatabaseSource = DatabaseSourceType.PostgreSQL,
            MasterConnectionString = connectionString,
            Timeout = 30
        };

    [Fact]
    public void 工厂按传入配置建会话()
    {
        var factory = new MomoDbContextFactory(new TestContext(), new RecordingLogger());

        using var db = factory.Create(Options("Host=custom"))!;

        // 上下文由这份 options 建；连接串来自它，而不是某个默认库
        Assert.Equal("Host=custom", db.GetEFContext(DbReadWriteType.Write).GetConnectionString());
    }

    [Fact]
    public void 租户访问器从当前上下文进入EF上下文()
    {
        var context = new TestContext();
        context.SetSnapshot(new VivContextContent { SubjectId = 7 });

        using var db = new MomoDatabaseContext(
            context, new RecordingLogger(),
            new StaticDatabaseOptionsProvider(Options()));

        var ef = db.GetEFContext(DbReadWriteType.Write);
        Assert.True(ef.HasTenantAccessor);
        Assert.Equal(7, ef.CurrentTenantId);
    }

    [Fact]
    public void 同一实例的写上下文只建一次()
    {
        using var db = new MomoDatabaseContext(
            new TestContext(), new RecordingLogger(),
            new StaticDatabaseOptionsProvider(Options()));

        Assert.Same(db.GetEFContext(DbReadWriteType.Write), db.GetEFContext(DbReadWriteType.Write));
    }

    /// <summary>
    /// 只保留一个公共构造。DatabaseOptions 本身也注册在 DI 里，再公开一个
    /// 参数个数相同的构造会让 Autofac 在解析 IMomoDbContext 时报歧义。
    /// </summary>
    [Fact]
    public void 公共构造只有一个_避免Autofac解析歧义()
    {
        Assert.Single(typeof(MomoDatabaseContext).GetConstructors());
        Assert.Single(typeof(MomoDatabase).GetConstructors());
    }
}
