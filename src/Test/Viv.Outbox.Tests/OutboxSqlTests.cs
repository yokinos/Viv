using Viv.Momo.Enums;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 手写 SQL 的静态检查。跑不了真库（CI 没有数据库），但几件「写错了必炸」的事能在字符串上钉死。
/// </summary>
public class OutboxSqlTests
{
    public static TheoryData<DatabaseSourceType> Sources =>
        new() { DatabaseSourceType.SqlServer, DatabaseSourceType.PostgreSQL };

    [Theory]
    [MemberData(nameof(Sources))]
    public void 建表脚本_能读到且是幂等的(DatabaseSourceType source)
    {
        var ddl = OutboxSql.CreateTable(source);

        Assert.False(string.IsNullOrWhiteSpace(ddl));

        // 多实例同时启动会各跑一次建表 —— 不幂等就是启动即炸
        var idempotent = ddl.Contains("IF OBJECT_ID", StringComparison.OrdinalIgnoreCase)
                         || ddl.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase);
        Assert.True(idempotent, "建表脚本没有幂等守卫");
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void 建表脚本_表名与全部列名都对得上(DatabaseSourceType source)
    {
        var ddl = OutboxSql.CreateTable(source);

        Assert.Contains("OutboxMessage", ddl);
        foreach (var column in Columns)
        {
            Assert.Contains(column, ddl);
        }
    }

    [Fact]
    public void 建表脚本_表名与列名一律不带引号()
    {
        foreach (var source in new[] { DatabaseSourceType.SqlServer, DatabaseSourceType.PostgreSQL })
        {
            var ddl = OutboxSql.CreateTable(source);

            // 带引号的话 PG 会保留大小写、SqlServer 未必 —— 同一份逻辑在两端漂移。
            // 不带引号才有「PG 折叠成小写、SqlServer 不区分」的天然一致。
            Assert.DoesNotContain("\"OutboxMessage\"", ddl);
            Assert.DoesNotContain("[OutboxMessage]", ddl);
            Assert.DoesNotContain("OutboxMessage\"", ddl);
        }
    }

    [Fact]
    public void 建表脚本_两种provider真的不一样()
    {
        // 防止哪天「顺手统一」成一份，把 TIMESTAMPTZ / TINYINT 这类方言差异一起抹掉
        Assert.NotEqual(
            OutboxSql.CreateTable(DatabaseSourceType.SqlServer),
            OutboxSql.CreateTable(DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void 插入语句_显式给Id_不依赖IDENTITY回填()
    {
        var sql = OutboxSql.Insert;

        Assert.Contains("@Id", sql);
        Assert.StartsWith("INSERT INTO OutboxMessage", sql, StringComparison.OrdinalIgnoreCase);

        // Id 由 IdMagic.NextId() 生成，所以 INSERT 不能漏掉它
        var columns = sql[sql.IndexOf('(')..sql.IndexOf(')')];
        Assert.Contains("Id", columns);
    }

    [Fact]
    public void 插入语句_Status与RetryCount由参数给_不在SQL里写死()
    {
        Assert.Contains("@Status", OutboxSql.Insert);
        Assert.Contains("@RetryCount", OutboxSql.Insert);

        // LeaseUntil / SentAt / LastError 入队时必须是 NULL
        Assert.Contains("NULL", OutboxSql.Insert);
    }

    [Fact]
    public void 释放过期租约_只碰Processing且只碰已过期的()
    {
        var sql = OutboxSql.ReleaseExpiredLeases;

        Assert.Contains("Status = 1", sql);           // 只认 Processing
        Assert.Contains("LeaseUntil <= @Now", sql);   // 只认已过期
        Assert.Contains("Status = 0", sql);           // 退回 Pending
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void 认领SQL_是原子认领而不是先查后改(DatabaseSourceType source)
    {
        var sql = OutboxSql.ClaimBatch(source);

        // 单条 UPDATE 把「判给谁」和「占住」做成一件事 —— 先 SELECT 再 UPDATE 会留一个窗口，
        // 两个实例各投一遍同一条消息（而且它编译通过、测试也通过）
        Assert.StartsWith("UPDATE OutboxMessage", sql, StringComparison.OrdinalIgnoreCase);

        // 认领的同时就盖上租约，否则崩溃后这批行永远卡在 Processing
        Assert.Contains("LeaseUntil = @LeaseUntil", sql);
        Assert.Contains("Status = 1", sql);
    }

    [Fact]
    public void 认领SQL_SqlServer用READPAST_PostgreSQL用SKIP_LOCKED()
    {
        var sqlServer = OutboxSql.ClaimBatch(DatabaseSourceType.SqlServer);
        var postgreSql = OutboxSql.ClaimBatch(DatabaseSourceType.PostgreSQL);

        Assert.Contains("WITH (READPAST)", sqlServer);
        Assert.DoesNotContain("SKIP LOCKED", sqlServer);

        Assert.Contains("FOR UPDATE SKIP LOCKED", postgreSql);
        Assert.DoesNotContain("READPAST", postgreSql);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void 认领SQL_带批大小与到期条件(DatabaseSourceType source)
    {
        var sql = OutboxSql.ClaimBatch(source);

        Assert.Contains("@BatchSize", sql);
        Assert.Contains("Status = 0", sql);          // 只认 Pending
        Assert.Contains("NextRetryAt <= @Now", sql); // 退避未到期的不能碰
    }

    [Fact]
    public void 认领SQL_两种provider都返回全部列并显式对齐列名()
    {
        var sqlServer = OutboxSql.ClaimBatch(DatabaseSourceType.SqlServer);
        var postgreSql = OutboxSql.ClaimBatch(DatabaseSourceType.PostgreSQL);

        Assert.Contains("OUTPUT inserted.", sqlServer);
        Assert.Contains("RETURNING", postgreSql);

        foreach (var source in new[] { DatabaseSourceType.SqlServer, DatabaseSourceType.PostgreSQL })
        {
            var sql = OutboxSql.ClaimBatch(source);
            foreach (var column in Columns)
            {
                // 每一列都显式对齐（inserted.X AS X / X AS "X"）：
                // 不把「Dapper 的列名映射在两种 provider 下是否一致」这个问题留到运行期
                Assert.Contains($"{column} AS", sql);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void 清理SQL_只删已发送且超保留期的(DatabaseSourceType source)
    {
        var sql = OutboxSql.CleanupBatch(source);

        Assert.StartsWith("DELETE FROM OutboxMessage", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Status = 2", sql);                      // 只删 Sent
        Assert.Contains("SentAt IS NOT NULL", sql);
        Assert.Contains("SentAt < @Cutoff", sql);

        // 必须分批：一把 DELETE 全表在积压时会长时间锁表
        Assert.Contains("@BatchSize", sql);
    }

    [Fact]
    public void 清理SQL_不会误删未发送与失败的行()
    {
        foreach (var source in new[] { DatabaseSourceType.SqlServer, DatabaseSourceType.PostgreSQL })
        {
            var sql = OutboxSql.CleanupBatch(source);

            // Failed(3) 是等人工介入的，删掉等于把问题一起删了
            Assert.DoesNotContain("Status = 3", sql);
            Assert.DoesNotContain("Status = 0", sql);
            Assert.DoesNotContain("Status = 1", sql);
        }
    }

    [Fact]
    public void 资源缺失_抛带资源名的异常而不是静默返回空串()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OutboxSql.ReadResource("Viv.Outbox.Sql.NoSuch.sql"));

        Assert.Contains("Viv.Outbox.Sql.NoSuch.sql", ex.Message);
    }

    private static readonly string[] Columns =
    [
        "Id", "MessageId", "EventType", "Payload", "Status", "RetryCount",
        "NextRetryAt", "LeaseUntil", "OccurredAt", "SentAt", "LastError",
    ];
}
