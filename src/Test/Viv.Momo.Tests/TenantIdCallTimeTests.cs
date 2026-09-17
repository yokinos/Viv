using Viv.Contracts;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// Dapper 路径 TenantId 必须调用时从 IVivContext 读取，不能在构造时冻结。
/// </summary>
public class TenantIdCallTimeTests
{
    [Fact]
    public void TenantId_构造后再SetSnapshot_读到新租户()
    {
        var ctx = new TestContext();
        var db = new MomoDatabase(ctx, new RecordingLogger(), new DefaultDatabaseOptionsProvider(XUnitTestMagic.CreateOptions(new DatabaseOptions() { Timeout = 30, MasterConnectionString = "x" })));

        Assert.Equal(0, db.TenantId);

        ctx.SetSnapshot(new VivContextContent { SubjectId = 77 });
        Assert.Equal(77, db.TenantId);
    }

    [Fact]
    public void ChangeTenant_覆盖本实例不影响后续上下文读取()
    {
        var ctx = new TestContext();
        ctx.SetSnapshot(new VivContextContent { SubjectId = 11 });
        var db = new MomoDatabaseContext(ctx, new RecordingLogger(), new DefaultDatabaseOptionsProvider(XUnitTestMagic.CreateOptions(new DatabaseOptions() { Timeout = 30, MasterConnectionString = "x" })));

        db.ChangeTenant(99);
        Assert.Equal(99, db.TenantId);

        ctx.SetSnapshot(new VivContextContent { SubjectId = 22 });
        Assert.Equal(99, db.TenantId);
    }
}
