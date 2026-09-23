using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Viv.Momo.Base;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Momo.Sync;

namespace Viv.Momo.Tests;

#region 测试实体
// 刻意都不以 "Entity" 结尾 —— 那个后缀会被 TenantFilterTests 的 EF 扫描
// （AssemblyName = "Viv.Momo.Tests", ClassNameEndsWith = "Entity"）捞进模型，污染它的断言。

/// <summary>只有 <c>Id</c>，没有任何 <c>[Key]</c> —— 全仓实体的真实形状</summary>
public class SyncConventionRow : IEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>显式 <c>[Key]</c> 且名字不叫 Id</summary>
public class SyncAttributedRow
{
    [Key]
    public Guid Code { get; set; }

    public string Label { get; set; } = "";
}

/// <summary>有外键风格属性 —— 用来证明不会把 <c>UserId</c> 误认成主键</summary>
public class SyncForeignKeyRow : IEntity
{
    public long Id { get; set; }
    public long UserId { get; set; }
}

/// <summary>导航属性 + 集合 + [NotMapped]，一个都不该变成列</summary>
public class SyncNavRow : IEntity
{
    public long Id { get; set; }
    public SyncConventionRow? Parent { get; set; }
    public ICollection<SyncConventionRow> Children { get; set; } = [];

    [NotMapped]
    public string Ignored { get; set; } = "";
}

/// <summary>继承 EntityBase（<c>[Key]</c> 标在基类的 <c>Id</c> 上）—— 全仓 41 个业务实体的真实形状</summary>
public class SyncBaseDerivedRow : EntityBase
{
    public string Title { get; set; } = "";
}

/// <summary>带表名/长度/可空时间 —— 「加列」路径的样本（Herta 那 4 列就是这形状）</summary>
[Table("sync_ddl_row")]
public class SyncDdlRow : IEntity
{
    public long Id { get; set; }

    [StringLength(50)]
    public string Code { get; set; } = "";

    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// 窄整数与无符号整数 —— AtCompanyAppRelation 那 12 个 <c>ulong</c> 掩码列的形状
/// （<c>ulong</c> 在 SqlServer 上没有原生类型，只能落到 <c>decimal(20,0)</c>），
/// 外加一个可空 <c>byte</c>（AtClientAppCarousel.Position）。
/// </summary>
[Table("sync_scalar_row")]
public class SyncScalarRow : IEntity
{
    public long Id { get; set; }
    public byte ByteCol { get; set; }
    public byte? NullableByteCol { get; set; }
    public sbyte SByteCol { get; set; }
    public ushort UShortCol { get; set; }
    public uint UIntCol { get; set; }
    public ulong ULongCol { get; set; }
    public char CharCol { get; set; }

    [Precision(20, 4)]
    public decimal PreciseAmount { get; set; }

    public decimal PlainAmount { get; set; }
}

#endregion

/// <summary>
/// 实体 → 建表/改表 SQL 的生成（<see cref="SchemaSynchronizer"/>）。
///
/// 这里测的全是纯反射 + 纯字符串逻辑，不碰数据库（<c>FetchActualSchemaAsync</c> 与
/// <c>ScanEntityTypes</c> 需要真库/真配置，不在覆盖范围内）。「实际 Schema」一律手工构造，
/// 所以这些测试钉的是「给定 diff 会生成什么 DDL」，不是「从真库读回来对不对」。
/// </summary>
public class SchemaSyncTests
{
    private static SchemaSynchronizer CreateSync(
        DatabaseSourceType dbType = DatabaseSourceType.SqlServer,
        bool allowAlterColumn = false)
        => new(new DatabaseOptions { DatabaseSource = dbType }, allowAlterColumn: allowAlterColumn);

    /// <summary>拿实体当「预期」、空库当「实际」→ 全部走新建表路径</summary>
    private static List<string> CreateTableDdl(DatabaseSourceType dbType, params Type[] types)
    {
        var sync = CreateSync(dbType);
        var expected = sync.BuildExpectedSchema([.. types]);
        return sync.GenerateDdl(sync.Diff(expected, []));
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        var index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }
        return count;
    }

    #region 主键判定

    [Fact]
    public void 主键判定_没标Key时按Id约定认()
    {
        // SyncConventionRow 不继承 EntityBase、也没标 [Key]，只有个叫 Id 的属性 ——
        // 只认特性的话这种表生成出来会一个主键都没有。
        var table = CreateSync().BuildExpectedSchema([typeof(SyncConventionRow)])[0];

        Assert.True(table.Columns.Single(c => c.Name == "Id").IsPrimaryKey);
        Assert.False(table.Columns.Single(c => c.Name == "Name").IsPrimaryKey);
    }

    [Fact]
    public void 主键判定_只认Id不认类名Id后缀()
    {
        // SyncForeignKeyRow.UserId 是外键不是主键。若照 EF 的 {类名}Id 约定认，
        // AtUserRoleRelation.UserId 这类属性会被误标成主键。
        var table = CreateSync().BuildExpectedSchema([typeof(SyncForeignKeyRow)])[0];

        Assert.True(table.Columns.Single(c => c.Name == "Id").IsPrimaryKey);
        Assert.False(table.Columns.Single(c => c.Name == "UserId").IsPrimaryKey);
    }

    [Fact]
    public void 主键判定_显式Key特性仍然生效()
    {
        var table = CreateSync().BuildExpectedSchema([typeof(SyncAttributedRow)])[0];

        Assert.True(table.Columns.Single(c => c.Name == "Code").IsPrimaryKey);
        Assert.False(table.Columns.Single(c => c.Name == "Label").IsPrimaryKey);
    }

    [Fact]
    public void 主键判定_继承EntityBase时基类上的Key特性被读到()
    {
        // [Key] 标在 EntityBase.Id 上（基类），派生类型经 GetProperties 拿到的那个 PropertyInfo
        // 的 DeclaringType 是 EntityBase —— Attribute.IsDefined 默认 inherit:true，读得到。
        var table = CreateSync().BuildExpectedSchema([typeof(SyncBaseDerivedRow)])[0];

        var id = table.Columns.Single(c => c.Name == "Id");
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);
        Assert.False(table.Columns.Single(c => c.Name == "Title").IsPrimaryKey);
    }

    [Fact]
    public void 新建表_EntityBase派生实体生成具名主键约束且只有一条()
    {
        // 真实业务实体走的就是这条：继承 EntityBase → [Key] → 表级具名约束，
        // 且不会跟内联 PRIMARY KEY 撞成两条（PG 那边会直接报 multiple primary keys）
        var sql = Assert.Single(CreateTableDdl(DatabaseSourceType.SqlServer, typeof(SyncBaseDerivedRow)));

        Assert.Contains("CONSTRAINT PK_SyncBaseDerivedRow PRIMARY KEY ([Id])", sql);
        Assert.Equal(1, CountOf(sql, "PRIMARY KEY"));
    }

    #endregion

    #region 导航属性

    [Fact]
    public void 构建预期Schema_跳过导航属性与NotMapped()
    {
        var table = CreateSync().BuildExpectedSchema([typeof(SyncNavRow)])[0];

        // Parent（IEntity 类型）、Children（集合）、Ignored（[NotMapped]）一个都不该成为列
        Assert.Equal(["Id"], table.Columns.Select(c => c.Name));
    }

    #endregion

    #region 新建表

    [Fact]
    public void 新建表_生成CREATE_TABLE_主键列NOT_NULL其余可空()
    {
        var ddl = CreateTableDdl(DatabaseSourceType.SqlServer, typeof(SyncConventionRow));

        var sql = Assert.Single(ddl);
        Assert.Contains("CREATE TABLE [SyncConventionRow]", sql);
        Assert.Contains("CONSTRAINT PK_SyncConventionRow PRIMARY KEY ([Id])", sql);

        // 只有主键显式 NOT NULL；可空列不写 NULL —— 省略即可空是两种方言的默认值
        Assert.Contains("[Id] bigint NOT NULL", sql);
        Assert.Contains("[Name] nvarchar(max)", sql);
        Assert.Contains("[Count] int", sql);
    }

    [Fact]
    public void 新建表_主键只有一条定义()
    {
        // 原先是「内联 PRIMARY KEY」+「表级 CONSTRAINT PK_...」两条并出 —— PG 直接
        // multiple primary keys 报错。这条钉死只留表级那一条（具名，后续可定位可删）。
        var ddl = CreateTableDdl(DatabaseSourceType.SqlServer, typeof(SyncConventionRow));

        Assert.Equal(1, CountOf(Assert.Single(ddl), "PRIMARY KEY"));
    }

    [Fact]
    public void 新建表_PostgreSQL方言表名列名转snake_case()
    {
        var ddl = CreateTableDdl(DatabaseSourceType.PostgreSQL, typeof(SyncConventionRow));

        var sql = Assert.Single(ddl);
        Assert.Contains("CREATE TABLE sync_convention_row", sql);
        Assert.Contains("id bigint NOT NULL", sql);
        Assert.Contains("name", sql);
        Assert.DoesNotContain("syncconventionrow", sql);
    }

    #endregion

    #region 加列（最常见的路径）

    /// <summary>表已存在、只缺 Code 与 CreatedAt 两列 —— 正是 Herta 那 16 张表的形状</summary>
    private static List<string> AddColumnDdl(DatabaseSourceType dbType, bool allowAlterColumn = false)
    {
        var sync = CreateSync(dbType, allowAlterColumn);
        var expected = sync.BuildExpectedSchema([typeof(SyncDdlRow)]);

        var existing = new List<TableInfo>
        {
            new()
            {
                Name = "sync_ddl_row",
                Columns = [new ColumnInfo { Name = "Id", SqlType = "bigint", IsNullable = false }]
            }
        };

        return sync.GenerateDdl(sync.Diff(expected, existing));
    }

    [Fact]
    public void 已存在的表_只加缺失的列()
    {
        var ddl = AddColumnDdl(DatabaseSourceType.SqlServer);

        Assert.Equal(2, ddl.Count);
        Assert.Contains("ALTER TABLE [sync_ddl_row] ADD [Code] nvarchar(50);", ddl);
        Assert.Contains("ALTER TABLE [sync_ddl_row] ADD [CreatedAt] datetime2;", ddl);
    }

    [Fact]
    public void 已存在的表_主键列已匹配不重复加()
    {
        // Id 在两边一致（类型 + 可空性）→ 不该出现在 DDL 里
        var ddl = AddColumnDdl(DatabaseSourceType.SqlServer);

        Assert.DoesNotContain(ddl, sql => sql.Contains("[Id]", StringComparison.Ordinal));
    }

    #endregion

    #region 改已有列（默认关）

    /// <summary>表已存在、列都在但 Code 的长度对不上 → 一条 Modified</summary>
    private static List<string> AlterColumnDdl(DatabaseSourceType dbType, bool allowAlterColumn)
    {
        var sync = CreateSync(dbType, allowAlterColumn);
        var expected = sync.BuildExpectedSchema([typeof(SyncDdlRow)]);

        var existingCodeType = dbType == DatabaseSourceType.PostgreSQL ? "varchar(200)" : "nvarchar(200)";
        var createdAtType = dbType == DatabaseSourceType.PostgreSQL ? "timestamp without time zone" : "datetime2";
        var idName = dbType == DatabaseSourceType.PostgreSQL ? "id" : "Id";
        var codeName = dbType == DatabaseSourceType.PostgreSQL ? "code" : "Code";
        var createdAtName = dbType == DatabaseSourceType.PostgreSQL ? "created_at" : "CreatedAt";

        var existing = new List<TableInfo>
        {
            new()
            {
                Name = "sync_ddl_row",
                Columns =
                [
                    new ColumnInfo { Name = idName, SqlType = "bigint", IsNullable = false },
                    new ColumnInfo { Name = codeName, SqlType = existingCodeType, IsNullable = true },
                    new ColumnInfo { Name = createdAtName, SqlType = createdAtType, IsNullable = true },
                ]
            }
        };

        return sync.GenerateDdl(sync.Diff(expected, existing));
    }

    [Fact]
    public void 改已有列_默认不生成ALTER_TABLE()
    {
        // 判据对现有库几乎全是误报：预期侧按 nonPkNullable=true 认为「除主键外全 NULL」、
        // string 无 [StringLength] 就是 nvarchar(max) —— 一比对就把有长度约束的列判成要放宽、
        // 把 NOT NULL 判成要去掉。allowDrop 那条门只管得到 Deleted，管不到 Modified。
        Assert.Empty(AlterColumnDdl(DatabaseSourceType.SqlServer, allowAlterColumn: false));
    }

    [Fact]
    public void 改已有列_显式开启后生成ALTER_TABLE()
    {
        var ddl = AlterColumnDdl(DatabaseSourceType.SqlServer, allowAlterColumn: true);

        Assert.Equal("ALTER TABLE [sync_ddl_row] ALTER COLUMN [Code] nvarchar(50) NULL;", Assert.Single(ddl));
    }

    [Fact]
    public void 改已有列_PostgreSQL一条语句两句子句()
    {
        var ddl = AlterColumnDdl(DatabaseSourceType.PostgreSQL, allowAlterColumn: true);

        Assert.Equal(
            "ALTER TABLE sync_ddl_row ALTER COLUMN code TYPE varchar(50), ALTER COLUMN code DROP NOT NULL;",
            Assert.Single(ddl));
    }

    #endregion

    #region 删列（门在调用方，不在生成器）

    [Fact]
    public void 删列_生成器照发DROP_DB里的多余列()
    {
        // GenerateDdl 不吞 Deleted —— 「要不要删」是 diff 级的开关（SyncTableAsync 的 allowDrop
        // 在调用它之前就把 Deleted 摘掉了）。这条钉死这个分工：生成器本身只管照 diff 出 SQL。
        var sync = CreateSync();
        var expected = sync.BuildExpectedSchema([typeof(SyncDdlRow)]);

        var existing = new List<TableInfo>
        {
            new()
            {
                Name = "sync_ddl_row",
                Columns =
                [
                    new ColumnInfo { Name = "Id", SqlType = "bigint", IsNullable = false },
                    new ColumnInfo { Name = "Code", SqlType = "nvarchar(50)", IsNullable = true },
                    new ColumnInfo { Name = "CreatedAt", SqlType = "datetime2", IsNullable = true },
                    new ColumnInfo { Name = "Legacy", SqlType = "nvarchar(max)", IsNullable = true },
                ]
            }
        };

        var ddl = sync.GenerateDdl(sync.Diff(expected, existing));

        Assert.Equal("ALTER TABLE [sync_ddl_row] DROP COLUMN IF EXISTS [Legacy];", Assert.Single(ddl));
    }

    #endregion

    #region 类型映射

    [Fact]
    public void 类型映射_无符号与窄整数不再落成字符串()
    {
        // 这两张映射表原先只有 long/int/short/bool/Guid/DateTime/decimal/double/float/TimeSpan，
        // byte / ulong 这些落进 GetValueOrDefault 的兜底 "nvarchar(max)" —— 建出来的是一列字符串，
        // 写入不报错、读出来是另一个东西。Apex 那 12 个 ulong 掩码列正是这个形状。
        var sql = Assert.Single(CreateTableDdl(DatabaseSourceType.SqlServer, typeof(SyncScalarRow)));

        Assert.Contains("[ULongCol] decimal(20,0)", sql);   // SqlServer 没有无符号整数，只能借 decimal 承载
        Assert.Contains("[ByteCol] tinyint", sql);
        Assert.Contains("[NullableByteCol] tinyint", sql);
        Assert.Contains("[SByteCol] smallint", sql);
        Assert.Contains("[UShortCol] int", sql);
        Assert.Contains("[UIntCol] bigint", sql);
        Assert.Contains("[CharCol] nvarchar(1)", sql);
        Assert.DoesNotContain("[ULongCol] nvarchar(max)", sql);   // 修之前就是这个形状
    }

    [Fact]
    public void 类型映射_PostgreSQL方言()
    {
        var sql = Assert.Single(CreateTableDdl(DatabaseSourceType.PostgreSQL, typeof(SyncScalarRow)));

        Assert.Contains("u_long_col numeric(20,0)", sql);
        Assert.Contains("byte_col smallint", sql);          // PG 的 smallint 就是 2 字节有符号
        Assert.Contains("u_short_col integer", sql);
        Assert.Contains("u_int_col bigint", sql);
        Assert.Contains("char_col character(1)", sql);      // Npgsql 把 char 落到 character(1)，不是 text
    }

    [Fact]
    public void 类型映射_decimal精度取自_Precision_特性()
    {
        var table = CreateSync().BuildExpectedSchema([typeof(SyncScalarRow)])[0];

        Assert.Equal("decimal(20,4)", table.Columns.Single(c => c.Name == "PreciseAmount").FullSqlType);
    }

    #endregion

    #region 与 EF 自身的映射对齐

    // 建表 DDL 的唯一目的，是建出一张 EF 认得的表。所以每一条映射都得跟 EF 自己算出来的列类型对上，
    // 对不上的表现是 DDL 建了 A 列、EF 读写时按 B 处理 —— 建表不报错、写入也不报错，最迟在读出
    // 一个不属于那个类型的东西时才发现。这两个上下文只读模型元数据（GetColumnType 由 provider
    // 在模型构建期算好），不连库。
    //
    // 这条同时是「ulong 该映射成什么」的判据来源：SqlServer 没有无符号整数，EF 把它落到
    // decimal(20,0)，映射表跟着它走，而不是反过来迁就一个好看的类型名。

    private sealed class ScalarSqlServerContext : DbContext
    {
        public DbSet<SyncScalarRow> Rows => Set<SyncScalarRow>();
        protected override void OnConfiguring(DbContextOptionsBuilder b) => b.UseSqlServer("Server=.");
    }

    private sealed class ScalarPgContext : DbContext
    {
        public DbSet<SyncScalarRow> Rows => Set<SyncScalarRow>();
        protected override void OnConfiguring(DbContextOptionsBuilder b) => b.UseNpgsql("Host=.");
    }

    private static void AssertMatchesEf(DbContext ctx, DatabaseSourceType dbType)
    {
        var entity = ctx.Model.FindEntityType(typeof(SyncScalarRow));
        Assert.NotNull(entity);

        var table = CreateSync(dbType).BuildExpectedSchema([typeof(SyncScalarRow)])[0];

        foreach (var prop in entity.GetProperties())
        {
            var column = table.Columns.Single(
                c => c.Name == Viv.Momo.MomoIdentifier.ToPhysical(prop.Name, dbType));
            Assert.Equal(prop.GetColumnType(), column.FullSqlType);
        }
    }

    [Fact]
    public void 类型映射_与SqlServer自己算出的列类型一致()
    {
        using var ctx = new ScalarSqlServerContext();
        AssertMatchesEf(ctx, DatabaseSourceType.SqlServer);
    }

    [Fact]
    public void 类型映射_与PostgreSQL自己算出的列类型一致()
    {
        using var ctx = new ScalarPgContext();
        AssertMatchesEf(ctx, DatabaseSourceType.PostgreSQL);
    }

    #endregion

    #region 实际列的归一化

    // INFORMATION_SCHEMA 的 DATA_TYPE 里没有精度：SqlServer 一律给 "decimal"，PG 给 "numeric"，
    // 精度在隔壁的 NUMERIC_PRECISION / NUMERIC_SCALE 两列上。不回读那两列、写死 numeric(18,2) 的话，
    // 凡精度不是 (18,2) 的列每次启动都被判成 Modified（ulong 的 (20,0)、[Precision] 标的 (20,4)），
    // 日志里挂着一条永远消不掉的假差异。

    [Fact]
    public void 归一化_SqlServer_decimal带回精度()
    {
        Assert.Equal("decimal(20,0)", SchemaSynchronizer.NormalizeSqlServerType("decimal", null, 20, 0));
        Assert.Equal("decimal(20,4)", SchemaSynchronizer.NormalizeSqlServerType("decimal", null, 20, 4));
    }

    [Fact]
    public void 归一化_PG_numeric带回精度()
    {
        Assert.Equal("numeric(20,0)", SchemaSynchronizer.NormalizePgType("numeric", null, 20, 0));
        Assert.Equal("numeric(18,2)", SchemaSynchronizer.NormalizePgType("numeric", null, 18, 2));
    }

    [Fact]
    public void 归一化_读不到精度时各按各的约定默认值()
    {
        // 两个 provider 对无约束 decimal 的约定不一样，这里跟着 EF 走而不是取同一个数：
        // SqlServer 的默认就是 decimal(18,2)，PG 的 numeric 无约束时不带精度。
        // 给 PG 也写 (18,2) 的话，建出来的列会把小数位静默截到两位。
        Assert.Equal("decimal(18,2)", SchemaSynchronizer.NormalizeSqlServerType("decimal", null, null, null));
        Assert.Equal("numeric", SchemaSynchronizer.NormalizePgType("numeric", null, null, null));
    }

    [Fact]
    public void 归一化_其余类型不受影响()
    {
        Assert.Equal("nvarchar(50)", SchemaSynchronizer.NormalizeSqlServerType("nvarchar", 50, null, null));
        Assert.Equal("nvarchar(max)", SchemaSynchronizer.NormalizeSqlServerType("nvarchar", null, null, null));
        Assert.Equal("tinyint", SchemaSynchronizer.NormalizeSqlServerType("tinyint", null, 3, 0));
        Assert.Equal("varchar(50)", SchemaSynchronizer.NormalizePgType("character varying", 50, null, null));
        Assert.Equal("text", SchemaSynchronizer.NormalizePgType("character varying", null, null, null));
        Assert.Equal("character(1)", SchemaSynchronizer.NormalizePgType("character", 1, null, null));
    }

    #endregion

    #region 表名匹配

    [Fact]
    public void Diff_表名忽略大小写_保留下划线()
    {
        var sync = CreateSync();
        var expected = new List<TableInfo> { new() { Name = "VivClientApp" } };
        var actual = new List<TableInfo> { new() { Name = "vivclientapp" } };

        var diff = sync.Diff(expected, actual);

        Assert.Empty(diff.NewTables);
        Assert.Empty(diff.DeletedTables);
        Assert.Empty(diff.ModifiedTables);
    }

    [Fact]
    public void Diff_下划线不同视为不同表()
    {
        // 以前靠去下划线模糊匹配把 EF snake_case 和 Dapper PascalCase 的差异藏掉。
        // 现在两边必须落到同一物理名，下划线是名字的一部分。
        var sync = CreateSync();
        var expected = new List<TableInfo> { new() { Name = "Viv_Client_App" } };
        var actual = new List<TableInfo> { new() { Name = "vivclientapp" } };

        var diff = sync.Diff(expected, actual);

        Assert.Single(diff.NewTables);
        Assert.Single(diff.DeletedTables);
    }

    #endregion
}
