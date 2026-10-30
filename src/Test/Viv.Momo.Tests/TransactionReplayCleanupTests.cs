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

        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

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

        MomoDatabase.RestoreTrackerAfterAttempt(context, MomoDatabase.SnapshotTrackedEntities(context));

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
        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

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

    [Fact]
    public void SaveChanges已接受的修改_失败后能再次被发现()
    {
        using var context = NewContext();

        // 事务外加载，尝试里改完并 SaveChanges（跟踪器已 Accept，库上的提交还没发生）
        var entity = new TenantUserEntity { Id = 3, Name = "旧", Age = 1 };
        context.Attach(entity);
        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        entity.Name = "新";
        entity.Age = 9;
        // SaveChanges 会先 DetectChanges 再 Accept。这里不碰库，把这两步分开做。
        context.ChangeTracker.DetectChanges();
        context.ChangeTracker.AcceptAllChanges();

        Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);

        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

        Assert.Equal("旧", entity.Name);
        Assert.Equal(1, entity.Age);
        Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);
        Assert.Equal("旧", context.Entry(entity).OriginalValues.GetValue<string>(nameof(TenantUserEntity.Name)));

        // 重试再赋同一个新值，必须重新变成 Modified，否则 SaveChanges 不发 UPDATE
        entity.Name = "新";
        Assert.Equal(EntityState.Modified, context.Entry(entity).State);
    }

    [Fact]
    public void SaveChanges已接受的新增_失败后摘掉_避免重试再插入一次()
    {
        using var context = NewContext();
        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        var created = new TenantUserEntity { Id = 8, Name = "本次" };
        context.Add(created);
        context.ChangeTracker.AcceptAllChanges();

        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

        Assert.Equal(EntityState.Detached, context.Entry(created).State);
    }

    [Fact]
    public void 尝试前已是Added_SaveChanges接受后_还原回Added()
    {
        using var context = NewContext();

        var outside = new TenantUserEntity { Id = 1, Name = "外部" };
        context.Add(outside);
        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        context.ChangeTracker.AcceptAllChanges();
        Assert.Equal(EntityState.Unchanged, context.Entry(outside).State);

        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

        Assert.Equal(EntityState.Added, context.Entry(outside).State);
        Assert.Equal("外部", outside.Name);
    }

    [Fact]
    public void 尝试中删除的实体_失败后回到删除前()
    {
        using var context = NewContext();

        var entity = new TenantUserEntity { Id = 6, Name = "删" };
        context.Attach(entity);
        var trackedBefore = MomoDatabase.SnapshotTrackedEntities(context);

        context.Remove(entity);
        Assert.Equal(EntityState.Deleted, context.Entry(entity).State);

        MomoDatabase.RestoreTrackerAfterAttempt(context, trackedBefore);

        Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);
        Assert.Equal("删", entity.Name);
    }
}
