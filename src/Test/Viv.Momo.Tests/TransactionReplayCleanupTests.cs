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
    public void 尝试期间新增的实体_无论是否已收成Unchanged都摘掉()
    {
        using var context = NewContext();

        // 事务之外就在跟踪里的（模拟 conversation 这种外部加载、事后还要 Update 的实体）
        var outsideEntity = new TenantUserEntity { Id = 1, Name = "外部" };
        context.Add(outsideEntity);

        var trackedBefore = MomoDatabase.SnapshotTracker(context);

        var stillAdded = new TenantUserEntity { Id = 2, Name = "还没保存" };
        context.Add(stillAdded);

        // SaveChanges 成功后状态是 Unchanged。回滚不会把它变回 Added，只摘 Added 会把它留在跟踪器里。
        var saved = new TenantUserEntity { Id = 3, Name = "已保存" };
        context.Add(saved);
        context.Entry(saved).State = EntityState.Unchanged;

        MomoDatabase.RestoreTracker(context, trackedBefore);

        Assert.Equal(EntityState.Added, context.Entry(outsideEntity).State);
        Assert.Equal(EntityState.Detached, context.Entry(stillAdded).State);
        Assert.Equal(EntityState.Detached, context.Entry(saved).State);
    }

    [Fact]
    public void 尝试前就在跟踪里的实体_值退回快照()
    {
        using var context = NewContext();

        var entity = new TenantUserEntity { Id = 3, Name = "旧", Age = 1 };
        context.Attach(entity);
        var trackedBefore = MomoDatabase.SnapshotTracker(context);

        // 模拟本次尝试里 Update + SaveChanges：当前值和原始值都被收成新值
        entity.Name = "新";
        entity.Age = 2;
        var entry = context.Entry(entity);
        entry.OriginalValues.SetValues(entity);
        entry.State = EntityState.Unchanged;

        MomoDatabase.RestoreTracker(context, trackedBefore);

        Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);
        Assert.Equal("旧", entity.Name);
        Assert.Equal(1, entity.Age);
        Assert.Equal("旧", context.Entry(entity).OriginalValues[nameof(TenantUserEntity.Name)]);
    }

    [Fact]
    public void 两次尝试后_只剩最后一次的实体()
    {
        using var context = NewContext();
        var trackedBefore = MomoDatabase.SnapshotTracker(context);

        var attempt1 = new TenantUserEntity { Id = 4 };
        context.Add(attempt1);
        context.Entry(attempt1).State = EntityState.Unchanged;
        MomoDatabase.RestoreTracker(context, trackedBefore);

        var attempt2 = new TenantUserEntity { Id = 5 };
        context.Add(attempt2);

        var added = context.ChangeTracker.Entries<TenantUserEntity>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        Assert.Single(added);
        Assert.Same(attempt2, added[0]);
    }
}
