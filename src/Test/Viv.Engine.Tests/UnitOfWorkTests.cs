using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Engine.UnitOfWork;
using Viv.Fakes;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 工作单元（<c>IVivUnitOfWork</c> + <c>UnitOfWorkManager</c>）—— 委托式事务：
/// 提交 / 异常回滚 / 失败信封回滚 / 嵌套合并 / 独立作用域。
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
    public async Task 正常返回_提交()
    {
        var (uow, kernel, _) = New();

        var result = await uow.ExecuteAsync(async _ =>
        {
            await Task.Yield();
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 抛异常_回滚并原样上抛()
    {
        var (uow, kernel, _) = New();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync<int>(_ => throw new InvalidOperationException("业务炸了")));

        Assert.Equal("业务炸了", ex.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));
    }

    [Fact]
    public async Task 提交判据返回false_回滚但仍返回结果()
    {
        var (uow, kernel, _) = New();

        // 「业务返回失败信封」：HTTP 仍 200，但写要回滚
        var result = await uow.ExecuteAsync(
            async _ => { await Task.Yield(); return "failed"; },
            commitWhen: r => r == "ok");

        Assert.Equal("failed", result);
        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 提交判据通过_提交()
    {
        var (uow, kernel, _) = New();

        var result = await uow.ExecuteAsync(
            async _ => { await Task.Yield(); return "ok"; },
            commitWhen: r => r == "ok");

        Assert.Equal("ok", result);
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 嵌套_只有最外层开事务和提交()
    {
        var (uow, kernel, _) = New();

        var result = await uow.ExecuteAsync(async _ =>
        {
            var inner = await uow.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return 1;
            });

            var inner2 = await uow.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return 2;
            });

            return inner + inner2;
        });

        Assert.Equal(3, result);
        Assert.Equal("begin|commit", kernel.Trace());
        Assert.Equal(1, kernel.Count("begin"));   // 嵌套不穿透到数据库
    }

    [Fact]
    public async Task 嵌套_内层抛异常_整体回滚()
    {
        var (uow, kernel, _) = New();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync<int>(async _ =>
            {
                await uow.ExecuteAsync<int>(_ => throw new InvalidOperationException("内层炸了"));
                return 1;
            }));

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));
    }

    [Fact]
    public async Task 嵌套_内层拒绝提交_外层回滚并抛出()
    {
        var (uow, kernel, _) = New();

        // 内层的失败信封必须拖垮整笔。只把结果交回外层、外层再返回成功的话，
        // 拦截器会当成成功，本地事件照样 Flush。
        var ex = await Assert.ThrowsAsync<VivUnitOfWorkException>(() =>
            uow.ExecuteAsync(async _ =>
            {
                var inner = await uow.ExecuteAsync(
                    async _ =>
                    {
                        await Task.Yield();
                        return "no";
                    },
                    commitWhen: static r => r == "ok");

                Assert.Equal("no", inner);
                await Task.Yield();
                return 1;
            }));

        Assert.Equal("内层事务已拒绝提交，整笔回滚。", ex.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(1, kernel.Count("begin"));
    }

    [Fact]
    public async Task 嵌套拒绝提交_不污染下一次事务()
    {
        var (uow, kernel, _) = New();

        await Assert.ThrowsAsync<VivUnitOfWorkException>(() =>
            uow.ExecuteAsync<int>(async _ =>
            {
                await uow.ExecuteAsync(async _ =>
                {
                    await Task.Yield();
                    return "no";
                }, commitWhen: static r => r == "ok");
                return 1;
            }));

        var result = await uow.ExecuteAsync(async _ =>
        {
            await Task.Yield();
            return 7;
        });

        Assert.Equal(7, result);
        Assert.Equal("begin|rollback|begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 连续事务_前一个回滚不污染后一个()
    {
        var (uow, kernel, _) = New();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync<int>(_ => throw new InvalidOperationException("第一次炸了")));

        var result = await uow.ExecuteAsync(async _ =>
        {
            await Task.Yield();
            return 7;
        });

        Assert.Equal(7, result);
        Assert.Equal("begin|rollback|begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 回滚失败_原异常优先()
    {
        var (uow, kernel, _) = New();
        kernel.RollbackException = new InvalidOperationException("回滚炸了");

        // 回滚通常在异常路径上补救：此刻再抛会掩盖触发回滚的那个原始异常
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync<int>(_ => throw new InvalidOperationException("业务炸了")));

        Assert.Equal("业务炸了", ex.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 提交失败_异常上抛且不补回滚()
    {
        var (uow, kernel, _) = New();
        kernel.CommitException = new InvalidOperationException("提交炸了");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync(async _ =>
            {
                await Task.Yield();
                return 1;
            }));

        // 提交失败必须响亮 —— 静默吞掉等于「业务以为写进去了、实际没有」
        Assert.Equal("提交炸了", ex.Message);
        Assert.Equal("begin|commit", kernel.Trace());
        Assert.Equal(0, kernel.Count("rollback"));
    }

    #region 独立事务（换作用域）

    /// <summary>
    /// 按 <c>VivRegister.RegisterDatabase</c> 的形状搭一个真容器：内核与工作单元都是 Scoped。
    /// 每个作用域新建的内核收进 <paramref name="kernels"/>，便于断言「两个作用域确实是两个内核」。
    /// </summary>
    private static ServiceProvider ScopedProvider(out List<KernelStub> kernels)
    {
        var created = new List<KernelStub>();
        kernels = created;

        var services = new ServiceCollection();
        services.AddScoped<ITransactionKernel>(_ =>
        {
            var kernel = new KernelStub();
            created.Add(kernel);
            return kernel;
        });
        services.AddScoped<IVivUnitOfWork, UnitOfWorkManager>();
        services.AddSingleton<ILoggerContract, RecordingLogger>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 换作用域 = 换 <c>IMomoDbContext</c> = 换连接 = 换事务状态机，内层那个是真独立事务。
    /// </summary>
    [Fact]
    public async Task 换作用域_内层提交与外层回滚互不影响()
    {
        using var provider = ScopedProvider(out var kernels);

        using var outerScope = provider.CreateScope();
        var outerUow = outerScope.ServiceProvider.GetRequiredService<IVivUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            outerUow.ExecuteAsync<int>(async _ =>
            {
                using (var innerScope = provider.CreateScope())
                {
                    var innerUow = innerScope.ServiceProvider.GetRequiredService<IVivUnitOfWork>();
                    await innerUow.ExecuteAsync(async _ => { await Task.Yield(); return 1; });
                }

                throw new InvalidOperationException("外层炸了");
            }));

        Assert.Equal(2, kernels.Count);                        // 两个作用域两个内核，没有共用
        Assert.NotSame(kernels[0], kernels[1]);
        Assert.Equal("begin|commit", kernels[1].Trace());      // 内层提交了
        Assert.Equal("begin|rollback", kernels[0].Trace());    // 外层照样回滚
    }

    [Fact]
    public async Task 换作用域_内层回滚不拖垮外层提交()
    {
        using var provider = ScopedProvider(out var kernels);

        using var outerScope = provider.CreateScope();
        var outerUow = outerScope.ServiceProvider.GetRequiredService<IVivUnitOfWork>();

        await outerUow.ExecuteAsync(async _ =>
        {
            using (var innerScope = provider.CreateScope())
            {
                var innerUow = innerScope.ServiceProvider.GetRequiredService<IVivUnitOfWork>();

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    innerUow.ExecuteAsync<int>(_ => throw new InvalidOperationException("内层炸了")));
            }

            await Task.Yield();
            return 1;
        });

        Assert.Equal("begin|rollback", kernels[1].Trace());
        Assert.Equal("begin|commit", kernels[0].Trace());
        Assert.Equal(0, kernels[0].Count("rollback"));   // 外层没有被连累着回滚
    }

    #endregion
}
