using Microsoft.EntityFrameworkCore;

using Viv.Delusion.Magic;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// 事务重放前的 ChangeTracker 清理（R1）。
///
/// 背景：开启 EF 自动重试后，一次失败尝试里 new 出来的实体仍留在 ChangeTracker（SaveChanges 失败不清状态），
/// 重放时 SaveChanges 会把它们和本次尝试的新实体一起 INSERT —— 重复消息、重复审批单。
/// 这里钉的是清理逻辑本身，不需要数据库。
/// </summary>
public class TransactionReplayCleanupTests
{
    private static DatabaseOptions Options()
        => new()
        {
            DatabaseSource = DatabaseSourceType.SqlServer,
            MasterConnectionString = "Server=localhost;Database=viv_test;Trusted_Connection=True;TrustServerCertificate=True",
            EntityTypeOptions =
            [
                new FilterTypeOptions { AssemblyName = "Viv.Momo.Tests", ClassNameEndsWith = "Entity" }
            ]
        };

    private static EFAppContext NewContext() => new(Options(), new TestContextAccessor(), DbReadWriteType.Write);

    [Fact]
    public void 只摘掉本次尝试新增的Added实体()
    {
        using var context = NewContext();

        // 事务之外就在跟踪里的（模拟 conversation 这种外部加载、事后还要 Update 的实体）
        var outsideEntity = new TenantUserEntity { Id = 1, Name = "外部" };
        context.Add(outsideEntity);

        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        // 本次尝试新增的
        var inAttempt = new TenantUserEntity { Id = 2, Name = "本次" };
        context.Add(inAttempt);

        MomoDatabase.DetachAddedSince(context, trackedBefore);

        Assert.Equal(EntityState.Added, context.Entry(outsideEntity).State);
        Assert.Equal(EntityState.Detached, context.Entry(inAttempt).State);
    }

    [Fact]
    public void 不动Modified实体()
    {
        using var context = NewContext();

        var entity = new TenantUserEntity { Id = 3, Name = "改" };
        context.Attach(entity);
        context.Entry(entity).State = EntityState.Modified;

        MomoDatabase.DetachAddedSince(context, MomoDatabase.SnapshotTrackedEntities(context));

        // Modified 是绝对赋值，重放赋同样的值就幂等，不能摘
        Assert.Equal(EntityState.Modified, context.Entry(entity).State);
    }

    [Fact]
    public void 两次尝试后_只剩最后一次的实体()
    {
        using var context = NewContext();
        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        // attempt 1：新增后失败，清理
        var attempt1 = new TenantUserEntity { Id = 4 };
        context.Add(attempt1);
        MomoDatabase.DetachAddedSince(context, trackedBefore);

        // attempt 2：重放，再 new 一个
        var attempt2 = new TenantUserEntity { Id = 5 };
        context.Add(attempt2);

        var added = context.ChangeTracker.Entries<TenantUserEntity>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        // 交付给 SaveChanges 的只有最后一次尝试的实体 —— 否则就是重复插入
        Assert.Single(added);
        Assert.Same(attempt2, added[0]);
    }
}
