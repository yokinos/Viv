using Viv.Momo;
using Viv.Momo.Dialect;
using Viv.Momo.Enums;
using Viv.Skirk.Core;

namespace Viv.Skirk.Tests;

/// <summary>
/// 双方言 SQL 的字符串级断言：Skirk 的 SQL 独立一份，不共享 Outbox、不塞进 ISqlDialect，
/// 所以这里钉住两边的物理名与语法差异（PG 的 snake_case 最容易写错）。
/// </summary>
public class VivAsyncTaskSqlTests
{
    private static readonly ISqlDialect Sql = SqlMagic.For(DatabaseSourceType.SqlServer);

    private static readonly ISqlDialect Pg = SqlMagic.For(DatabaseSourceType.PostgreSQL);

    [Fact]
    public void 建表_SQLServer用OBJECT_ID与方括号()
    {
        var statements = VivAsyncTaskSql.CreateTable(Sql);

        Assert.Equal(4, statements.Count);
        Assert.Contains("IF OBJECT_ID(N'VivAsyncTask', N'U') IS NULL", statements[0]);
        Assert.Contains("[TaskType]", statements[0]);
        Assert.Contains("[Id] bigint NOT NULL", statements[0]);
        Assert.Contains("PK_VivAsyncTask", statements[0]);
    }

    [Fact]
    public void 建表_Postgres用IF_NOT_EXISTS与snake_case()
    {
        var statements = VivAsyncTaskSql.CreateTable(Pg);

        Assert.Equal(4, statements.Count);
        Assert.Contains("CREATE TABLE IF NOT EXISTS viv_async_task", statements[0]);
        Assert.Contains("task_type varchar(64)", statements[0]);
        Assert.Contains("params_json text", statements[0]);
        Assert.Contains("cancel_requested boolean", statements[0]);
        Assert.Contains("status integer", statements[0]);
        Assert.Contains("timestamptz", statements[0]);
        Assert.DoesNotContain(" timestamp ", statements[0]);
    }

    [Fact]
    public void 唯一幂等键索引_带过滤条件()
    {
        var pgIndex = VivAsyncTaskSql.CreateTable(Pg)[1];
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS", pgIndex);
        Assert.Contains("WHERE idempotency_key IS NOT NULL", pgIndex);

        var sqlServerIndex = VivAsyncTaskSql.CreateTable(Sql)[1];
        Assert.Contains("WHERE [IdempotencyKey] IS NOT NULL", sqlServerIndex);
    }

    [Fact]
    public void 认领_SQLServer用READPAST与OUTPUT()
    {
        var sql = VivAsyncTaskSql.ClaimBatch(Sql);

        Assert.Contains("WITH (UPDLOCK, READPAST, ROWLOCK)", sql);
        Assert.Contains("OUTPUT INSERTED.*", sql);
        Assert.Contains("AND [Status] = @Pending", sql);
        Assert.Contains("SELECT TOP (@BatchSize)", sql);
        Assert.Contains("[Status] = @Running", sql);
        Assert.DoesNotContain("SKIP LOCKED", sql);
    }

    [Fact]
    public void 认领_Postgres用SKIP_LOCKED与RETURNING()
    {
        var sql = VivAsyncTaskSql.ClaimBatch(Pg);

        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("RETURNING *", sql);
        Assert.Contains("AND status = @Pending", sql);
        Assert.Contains("LIMIT @BatchSize", sql);
        Assert.Contains("status = @Running", sql);
        Assert.DoesNotContain("READPAST", sql);
    }

    [Fact]
    public void 认领条件_只取到期且未租用的待执行行()
    {
        foreach (var dialect in new[] { Sql, Pg })
        {
            var sql = VivAsyncTaskSql.ClaimBatch(dialect);

            Assert.Contains("@Status = @Pending".Replace("@Status", $"{dialect.QuoteClr("Status")}"), sql);
            Assert.Contains("NextAttemptAt", sql.Replace("next_attempt_at", "NextAttemptAt"));
            Assert.Contains("LeaseUntil", sql.Replace("lease_until", "LeaseUntil"));
        }
    }

    [Fact]
    public void 续租与完成_都带租约持有者条件()
    {
        Assert.Contains("LeaseOwner", VivAsyncTaskSql.RenewLease(Sql).Replace("[LeaseOwner]", "LeaseOwner"));
        Assert.Contains("@LeaseOwner", VivAsyncTaskSql.RenewLease(Sql));
        Assert.Contains("@LeaseOwner", VivAsyncTaskSql.Complete(Sql));
        Assert.Contains("@LeaseOwner", VivAsyncTaskSql.Fail(Sql));
    }

    [Fact]
    public void 列表查询_分页语法按方言()
    {
        // SQL Server：OFFSET / FETCH（2012+ 的标准写法）
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", VivAsyncTaskSql.QueryItems(Sql));
        Assert.Contains("ORDER BY [CreatedAt] DESC", VivAsyncTaskSql.QueryItems(Sql));

        // PostgreSQL：LIMIT / OFFSET
        Assert.Contains("LIMIT @PageSize OFFSET @Offset", VivAsyncTaskSql.QueryItems(Pg));
        Assert.Contains("ORDER BY created_at DESC", VivAsyncTaskSql.QueryItems(Pg));
    }

    [Fact]
    public void 总数查询_与列表条件一致且不带排序()
    {
        var countSql = VivAsyncTaskSql.QueryCount(Sql);

        Assert.StartsWith("SELECT COUNT(*) FROM", countSql);
        Assert.DoesNotContain("ORDER BY", countSql);
        Assert.Contains("@SubjectId IS NULL", countSql);
        Assert.Contains("@UserId IS NULL", countSql);
        Assert.Contains("@TaskType IS NULL", countSql);
        Assert.Contains("@Status IS NULL", countSql);

        // 两个方言的条件必须完全一致，只有分页尾巴不同
        Assert.Contains("@SubjectId IS NULL", VivAsyncTaskSql.QueryCount(Pg));
        Assert.DoesNotContain("ORDER BY", VivAsyncTaskSql.QueryCount(Pg));
    }

    [Fact]
    public void 清理_按方言限制删除条数()
    {
        Assert.Contains("SELECT TOP (@BatchSize)", VivAsyncTaskSql.Cleanup(Sql));
        Assert.Contains("LIMIT @BatchSize", VivAsyncTaskSql.Cleanup(Pg));
        Assert.Contains("ExpireAt", VivAsyncTaskSql.Cleanup(Sql).Replace("[ExpireAt]", "ExpireAt"));
    }

    [Fact]
    public void 取消只对非终态生效()
    {
        var sql = VivAsyncTaskSql.RequestCancel(Sql);

        Assert.Contains("CancelRequested", sql.Replace("[CancelRequested]", "CancelRequested"));
        Assert.Contains("@CancelRequested", sql);
        Assert.DoesNotContain("= 1", sql);
        Assert.Contains("@Pending", sql);
        Assert.Contains("@Running", sql);

        var pg = VivAsyncTaskSql.RequestCancel(Pg);
        Assert.Contains("cancel_requested = @CancelRequested", pg);
        Assert.DoesNotContain("= 1", pg);
    }

    [Fact]
    public void 入队_Postgres用ON_CONFLICT吞掉幂等冲突()
    {
        var pg = VivAsyncTaskSql.Insert(Pg);
        Assert.Contains("ON CONFLICT (idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING", pg);

        var sql = VivAsyncTaskSql.Insert(Sql);
        Assert.DoesNotContain("ON CONFLICT", sql);
    }

    [Fact]
    public void 回收过期租约_超过重试上限标失败()
    {
        foreach (var dialect in new[] { Sql, Pg })
        {
            var sql = VivAsyncTaskSql.ReleaseExpiredLeases(dialect);
            Assert.Contains("@Failed", sql);
            Assert.Contains("@Pending", sql);
            Assert.Contains("MaxAttempts", sql.Replace("max_attempts", "MaxAttempts"));
        }
    }
}
