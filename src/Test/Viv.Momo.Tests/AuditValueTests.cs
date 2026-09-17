using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Momo.Interface;

namespace Viv.Momo.Tests;

#region 测试行
// ⚠️ 刻意都不以 "Entity" 结尾 —— 那个后缀会被 TenantFilterTests 的 EF 扫描
// （AssemblyName = "Viv.Momo.Tests", ClassNameEndsWith = "Entity"）捞进模型，污染它的断言。

/// <summary>完整四件套 + 租户</summary>
public class FullAuditRow : IEntity, ITenant, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>只关心创建 —— 更新路径必须完全不碰它</summary>
public class CreateOnlyRow : IEntity, ICreatedAt, ICreatedBy
{
    public long Id { get; set; }
    public DateTime? CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}

/// <summary>只关心更新</summary>
public class UpdateOnlyRow : IEntity, IUpdatedAt, IUpdatedBy
{
    public long Id { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>一个审计接口都不实现 —— 框架一个字段都不该碰（Herta 那批实体就是这样）</summary>
public class NoAuditRow : IEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion

/// <summary>
/// 审计字段自动填充（<c>MomoDatabase.AutoSetInsertValue</c> / <c>AutoSetUpdateValue</c>）与
/// 更新路径的不可变列保护（<c>CopyProtectedValues</c>）。
///
/// <para>
/// ⚠️ 这里测的全是纯内存逻辑。真正的落库路径（<c>Entry(existing).CurrentValues.SetValues</c> +
/// <c>SaveChanges</c>）需要数据库，CI 没有 —— 那一段没有被这些测试覆盖，别把它们当成端到端验证。
/// </para>
/// </summary>
public class AuditValueTests
{
    private static readonly DateTime UserIdSentinel = new(2000, 1, 1);

    private static MomoAuditSut CreateSut(long userId = 9, long subjectId = 7)
    {
        var context = new TestContext();
        if (userId != 0 || subjectId != 0)
        {
            context.Snapshot = new VivContextContent { UserId = userId, SubjectId = subjectId };
        }

        return new MomoAuditSut(context);
    }

    #region 新增

    [Fact]
    public void 新增_四件套一起盖_时间取UtcNow_操作人取登录用户()
    {
        var sut = CreateSut(userId: 9);
        var row = new FullAuditRow();

        var before = DateTime.UtcNow;
        sut.AutoSetInsert(row);
        var after = DateTime.UtcNow;

        Assert.InRange(row.CreatedAt!.Value, before, after);
        Assert.InRange(row.UpdatedAt!.Value, before, after);
        Assert.Equal(9L, row.CreatedBy);
        Assert.Equal(9L, row.UpdatedBy);
    }

    [Fact]
    public void 新增_创建与更新时间取同一个时刻()
    {
        // 只插不改的行靠这一步才能有更新时间；两个字段取两次 UtcNow 会产生无意义的微小差异
        var row = new FullAuditRow();

        CreateSut().AutoSetInsert(row);

        Assert.Equal(row.CreatedAt, row.UpdatedAt);
    }

    [Fact]
    public void 新增_只实现部分接口_只填实现的那几个()
    {
        var sut = CreateSut(userId: 9);
        var createOnly = new CreateOnlyRow();
        var updateOnly = new UpdateOnlyRow();

        sut.AutoSetInsert(createOnly);
        sut.AutoSetInsert(updateOnly);

        Assert.NotNull(createOnly.CreatedAt);
        Assert.Equal(9L, createOnly.CreatedBy);

        // 反向：只实现更新的行，新增时不该被盖创建信息（它压根没有那两个字段，
        // 这里断言的是「没实现的接口不会被凭空补齐」这个契约在类型系统里的体现）
        Assert.NotNull(updateOnly.UpdatedAt);
        Assert.Equal(9L, updateOnly.UpdatedBy);
    }

    [Fact]
    public void 新增_一个审计接口都没实现_一个字段都不碰()
    {
        var row = new NoAuditRow { Name = "x" };

        CreateSut().AutoSetInsert(row);

        // Id 该照旧填（那是既有行为，不是审计）
        Assert.NotEqual(0L, row.Id);
        Assert.Equal("x", row.Name);
    }

    [Fact]
    public void 新增_无登录上下文_操作人记null而不是0()
    {
        var sut = CreateSut(userId: 0, subjectId: 7);
        var row = new FullAuditRow();

        sut.AutoSetInsert(row);

        // 0 是「没有操作人」；记 0 会跟真实存在的 UserId = 0 混在一起
        Assert.Null(row.CreatedBy);
        Assert.Null(row.UpdatedBy);
        Assert.Null(sut.CurrentUserIdForTest);

        // 租户不受影响 —— 它是 SubjectId，跟登录用户是两回事
        Assert.Equal(7L, row.TenantId);
    }

    [Fact]
    public void 新增_操作人取登录用户而非租户主体()
    {
        // SubjectId=7（租户/组织），UserId=9（人）—— 审计要的是人
        var sut = CreateSut(userId: 9, subjectId: 7);
        var row = new FullAuditRow();

        sut.AutoSetInsert(row);

        Assert.Equal(9L, row.CreatedBy);
        Assert.Equal(7L, row.TenantId);
    }

    [Fact]
    public void 新增_Id与租户的既有行为不变()
    {
        var sut = CreateSut(userId: 9, subjectId: 7);
        var row = new FullAuditRow();

        sut.AutoSetInsert(row);

        Assert.NotEqual(0L, row.Id);
        Assert.Equal(7L, row.TenantId);
    }

    [Fact]
    public void 新增_已填的Id与租户不被覆盖()
    {
        var sut = CreateSut(userId: 9, subjectId: 7);
        var row = new FullAuditRow { Id = 12345, TenantId = 99 };

        sut.AutoSetInsert(row);

        Assert.Equal(12345L, row.Id);
        Assert.Equal(99L, row.TenantId);
    }

    [Fact]
    public void 新增_自动填充关闭时一个字段都不填()
    {
        var sut = CreateSut(userId: 9, subjectId: 7);
        sut.DisableAutoSetValue();
        var row = new FullAuditRow();

        sut.AutoSetInsert(row);

        Assert.Null(row.CreatedAt);
        Assert.Null(row.CreatedBy);
        Assert.Null(row.UpdatedAt);
        Assert.Null(row.UpdatedBy);
        Assert.Equal(0L, row.Id);
        Assert.Equal(0L, row.TenantId);
    }

    #endregion

    #region 更新

    [Fact]
    public void 更新_只盖更新时间与人()
    {
        var sut = CreateSut(userId: 9);
        var row = new FullAuditRow();

        var before = DateTime.UtcNow;
        sut.AutoSetUpdate(row);
        var after = DateTime.UtcNow;

        Assert.InRange(row.UpdatedAt!.Value, before, after);
        Assert.Equal(9L, row.UpdatedBy);
    }

    [Fact]
    public void 更新_绝不碰创建信息()
    {
        var sut = CreateSut(userId: 9);
        var row = new FullAuditRow
        {
            CreatedAt = UserIdSentinel,
            CreatedBy = 42,
            UpdatedAt = UserIdSentinel,
            UpdatedBy = 42,
        };

        sut.AutoSetUpdate(row);

        // 创建信息是只写一次的 —— 更新路径一个字节都不该动它
        Assert.Equal(UserIdSentinel, row.CreatedAt);
        Assert.Equal(42L, row.CreatedBy);

        // 更新信息则无条件刷新（不是「为 default 才填」）
        Assert.NotEqual(UserIdSentinel, row.UpdatedAt);
        Assert.Equal(9L, row.UpdatedBy);
    }

    [Fact]
    public void 更新_只实现创建能力的行完全不动()
    {
        var row = new CreateOnlyRow { CreatedAt = UserIdSentinel, CreatedBy = 42 };

        CreateSut().AutoSetUpdate(row);

        Assert.Equal(UserIdSentinel, row.CreatedAt);
        Assert.Equal(42L, row.CreatedBy);
    }

    [Fact]
    public void 更新_无登录上下文_操作人记null而不是0()
    {
        var sut = CreateSut(userId: 0, subjectId: 7);
        var row = new FullAuditRow { UpdatedBy = 42 };

        sut.AutoSetUpdate(row);

        // 无条件刷新 → 上一次的操作人要被清成 null，而不是留着旧值
        Assert.Null(row.UpdatedBy);
        Assert.NotNull(row.UpdatedAt);
    }

    [Fact]
    public void 更新_自动填充关闭时一个字段都不填()
    {
        var sut = CreateSut(userId: 9);
        sut.DisableAutoSetValue();
        var row = new FullAuditRow { UpdatedAt = UserIdSentinel, UpdatedBy = 42 };

        sut.AutoSetUpdate(row);

        Assert.Equal(UserIdSentinel, row.UpdatedAt);
        Assert.Equal(42L, row.UpdatedBy);
    }

    #endregion

    #region 不可变列保护

    [Fact]
    public void 保护_把创建信息从库里那份补回入参()
    {
        // 库里加载出来的那份带着真实创建信息
        var existing = new FullAuditRow { Id = 1, CreatedAt = UserIdSentinel, CreatedBy = 42 };
        // 调用方传进来的那份是 default —— SetValues 会把 default 覆盖上去
        var incoming = new FullAuditRow { Id = 1, CreatedAt = null, CreatedBy = null };

        MomoAuditSut.PreserveProtectedValues(existing, incoming);

        Assert.Equal(UserIdSentinel, incoming.CreatedAt);
        Assert.Equal(42L, incoming.CreatedBy);
    }

    [Fact]
    public void 保护_把租户从库里那份补回入参()
    {
        // 没填租户的入参会把行搬到租户 0 —— 而全局查询过滤器立刻让它在业务侧「消失」
        var existing = new FullAuditRow { Id = 1, TenantId = 7 };
        var incoming = new FullAuditRow { Id = 1, TenantId = 0 };

        MomoAuditSut.PreserveProtectedValues(existing, incoming);

        Assert.Equal(7L, incoming.TenantId);
    }

    [Fact]
    public void 保护_不碰更新信息()
    {
        // 更新信息是要被入参覆盖的（那正是盖章的出口），保护它会把每次更新都盖不上
        var existing = new FullAuditRow { Id = 1, UpdatedAt = UserIdSentinel, UpdatedBy = 42 };
        var incoming = new FullAuditRow { Id = 1, UpdatedAt = null, UpdatedBy = null };

        MomoAuditSut.PreserveProtectedValues(existing, incoming);

        Assert.Null(incoming.UpdatedAt);
        Assert.Null(incoming.UpdatedBy);
    }

    [Fact]
    public void 保护_单边实现接口时不抛异常()
    {
        // from 有创建信息、to 没有那两个字段（或反之）—— 两个方向的 is 判断都得挡得住
        var existing = new FullAuditRow { Id = 1, CreatedAt = UserIdSentinel, TenantId = 7 };
        var incoming = new NoAuditRow { Id = 1 };

        MomoAuditSut.PreserveProtectedValues(existing, incoming);

        var reverseExisting = new NoAuditRow { Id = 1 };
        var reverseIncoming = new FullAuditRow { Id = 1 };

        MomoAuditSut.PreserveProtectedValues(reverseExisting, reverseIncoming);

        Assert.Null(reverseIncoming.CreatedAt);
        Assert.Equal(0L, reverseIncoming.TenantId);
    }

    #endregion
}
