using Viv.Engine.UnitOfWork;

namespace Viv.Engine.Tests;

/// <summary>
/// 窄事务（<c>IVivTransaction</c> + <c>UnitOfWorkManager</c>）—— 提交 / 自动回滚 / 幂等 / 嵌套 / 粘性回滚标记。
/// 零数据库：内核是手写桩，只记调用序列。
///
/// 归到 "UnitOfWork" 集合：本组测试与拦截器组、注册校验组都会碰
/// <see cref="UnitOfWorkDiagnostics"/> 的进程级一次性静态状态，必须串行。
/// </summary>
[Collection("UnitOfWork")]
public class UnitOfWorkTests
{
    private static (UnitOfWorkManager Uow, KernelStub Kernel, RecordingLogger Logger) New()
    {
        var kernel = new KernelStub();
        var logger = new RecordingLogger();
        return (new UnitOfWorkManager(kernel, logger), kernel, logger);
    }

    [Fact]
    public async Task 窄事务_显式提交后调用Commit()
    {
        var (uow, kernel, _) = New();

        await using (var tx = await uow.BeginAsync())
        {
            await tx.CommitAsync();
        }

        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 窄事务_未提交释放_自动回滚()
    {
        var (uow, kernel, _) = New();

        await using (var tx = await uow.BeginAsync())
        {
            // 刻意不提交 —— 离开作用域就是回滚
        }

        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 窄事务_显式回滚_只回滚不提交()
    {
        var (uow, kernel, _) = New();

        await using (var tx = await uow.BeginAsync())
        {
            await tx.RollbackAsync();
        }

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));
    }

    [Fact]
    public async Task 幂等_重复提交只落一次()
    {
        var (uow, kernel, _) = New();

        await using (var tx = await uow.BeginAsync())
        {
            await tx.CommitAsync();
            await tx.CommitAsync();
        }

        // 释放时也不能再补一次 —— 「await using 里显式提交」是常规写法，不能重复动作
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 已完成句柄再回滚_完全无效且不污染下一个事务()
    {
        var (uow, kernel, _) = New();

        await using (var tx = await uow.BeginAsync())
        {
            await tx.CommitAsync();
            await tx.RollbackAsync();          // 已结束的句柄，应当彻底无效
        }

        // ★ 回归：回滚若在「已完成」判断之前就把作用域打成 rollback-only，
        //   同作用域里的下一个事务会带着这个标记跑 —— 业务以为提交了、实际静默回滚。
        await using (var second = await uow.BeginAsync())
        {
            await second.CommitAsync();
        }

        Assert.Equal("begin|commit|begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 嵌套_只有最外层提交()
    {
        var (uow, kernel, _) = New();

        await using (var outer = await uow.BeginAsync())
        {
            await using (var inner = await uow.BeginAsync())
            {
                await inner.CommitAsync();     // 子句柄：空操作，等最外层
            }

            await outer.CommitAsync();
        }

        Assert.Equal("begin|commit", kernel.Trace());
        Assert.Equal(1, kernel.Count("begin"));   // 嵌套不穿透到数据库
    }

    [Fact]
    public async Task 嵌套_内层未提交_外层提交被降级为回滚()
    {
        var (uow, kernel, logger) = New();

        await using (var outer = await uow.BeginAsync())
        {
            await using (var inner = await uow.BeginAsync())
            {
                // 内层没提交就结束 —— 粘性回滚标记（没有保存点，整个事务一起回滚）
            }

            await outer.CommitAsync();
        }

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));

        // 不静默：业务以为提交了、实际回滚了，是排查成本最高的一类问题
        Assert.Contains(logger.Warnings, w => w.Contains("降级为回滚"));
    }

    [Fact]
    public async Task 嵌套_内层显式回滚_外层提交被降级为回滚()
    {
        var (uow, kernel, _) = New();

        await using (var outer = await uow.BeginAsync())
        {
            await using (var inner = await uow.BeginAsync())
            {
                await inner.RollbackAsync();
            }

            await outer.CommitAsync();
        }

        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 嵌套_内层抛异常_整体回滚()
    {
        var (uow, kernel, _) = New();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var outer = await uow.BeginAsync();

            await using (var inner = await uow.BeginAsync())
            {
                throw new InvalidOperationException("内层炸了");
            }
        });

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));
    }

    [Fact]
    public async Task 顺序事务_前一个被降级回滚_不污染后一个()
    {
        var (uow, kernel, _) = New();

        await using (var first = await uow.BeginAsync())
        {
            await using (var inner = await uow.BeginAsync())
            {
                // 内层不提交 → 整个作用域只能回滚
            }

            await first.CommitAsync();     // 降级为回滚
        }

        await using (var second = await uow.BeginAsync())
        {
            await second.CommitAsync();
        }

        Assert.Equal("begin|rollback|begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 开启失败_抛异常且不留半截状态()
    {
        var (uow, kernel, _) = New();
        kernel.BeginResult = false;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => uow.BeginAsync());
        Assert.Contains("事务开启失败", ex.Message);

        // 记账必须没发生 —— 否则下一句 BeginAsync 会以为事务已经开着，再也不开真的
        kernel.BeginResult = true;
        await using (var tx = await uow.BeginAsync())
        {
            await tx.CommitAsync();
        }

        Assert.Equal("begin|begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 回滚失败_只记Error不抛()
    {
        var (uow, kernel, logger) = New();
        kernel.RollbackException = new InvalidOperationException("回滚炸了");

        // 不抛 —— 回滚通常在异常路径上补救，此刻再抛会掩盖触发回滚的那个原始异常
        await using (var tx = await uow.BeginAsync())
        {
        }

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Contains(logger.Errors, e => e.Contains("回滚失败"));
    }

    [Fact]
    public async Task 提交失败_异常响亮上抛且不吞()
    {
        var (uow, kernel, _) = New();
        kernel.CommitException = new InvalidOperationException("提交炸了");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var tx = await uow.BeginAsync();
            await tx.CommitAsync();
        });

        // 提交失败必须响亮（Momo 会包成 VivConnectionException 抛出来）——
        // 静默吞掉等于「业务以为写进去了、实际没有」
        Assert.Equal("提交炸了", ex.Message);
        Assert.Equal("begin|commit", kernel.Trace());
        Assert.Equal(0, kernel.Count("rollback"));   // 提交失败不自动补回滚，让异常原样冒到上层
    }
}
