using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Fakes;
using Viv.Momo.Core;

namespace Viv.Momo.Tests;

/// <summary>
/// 重试识别（评审点 5）。
///
/// EF 的执行策略靠「异常链里有没有 SqlException/PostgresException」判断是否重试，
/// 但它的剥壳（<see cref="ExecutionStrategy.CallOnWrappedException"/>）只穿过 **EF 自己创建的**异常。
/// `VivConnectionException` 是框架包装，剥壳停在它上面 → 策略认为不可重试 → EnableRetryOnFailure 白挂。
///
/// 这里的用例钉住两件事：① EF 的剥壳确实停在包装上（如果哪天变了，这套绕过可以删）；
/// ② 我们的 <see cref="MomoDatabase.FindRetryableDbException"/> 能穿过包装把原始 DbException 找出来。
/// </summary>
public class RetryUnwrapTests
{
    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message) { }
    }

    /// <summary>模拟 SQL Server 的一条 DbUpdateException → DbException 链，外面再套 Viv 包装</summary>
    private static (VivConnectionException Wrapped, DbException Db) BuildChain()
    {
        var db = new FakeDbException("1205 死锁");
        var wrapped = new VivConnectionException(
            VivConnType.SqlServer,
            "InsertAsync, 死锁",
            new DbUpdateException("更新失败", db));
        return (wrapped, db);
    }

    [Fact]
    public void EF剥壳停在Viv包装上_只能看到包装本身()
    {
        var (wrapped, _) = BuildChain();

        var seen = new List<Exception>();
        ExecutionStrategy.CallOnWrappedException<object?>(wrapped, e =>
        {
            seen.Add(e);
            return null;
        });

        // 正是因为这个"停下"，才需要我们自己剥
        Assert.Single(seen);
        Assert.Same(wrapped, seen[0]);
    }

    [Fact]
    public void 能穿过Viv包装找到原始DbException()
    {
        var (wrapped, db) = BuildChain();

        Assert.Same(db, MomoDatabase.FindRetryableDbException(wrapped));
    }

    [Fact]
    public void 异常链里没有DbException时返回null()
    {
        var wrapped = new VivConnectionException(VivConnType.Redis, "缓存失败", new InvalidOperationException("缓存挂了"));

        Assert.Null(MomoDatabase.FindRetryableDbException(wrapped));
    }

    [Fact]
    public void 直接传DbException时返回它自己()
    {
        var db = new FakeDbException("连接断开");

        Assert.Same(db, MomoDatabase.FindRetryableDbException(db));
    }
}
