using Viv.Contracts.Attributes;
using Viv.Engine.UnitOfWork;
using Viv.Nana;

namespace Viv.Engine.Tests;

/// <summary>
/// 工作单元的注册期筛选与启动期校验。
///
/// 【为什么校验这么重】Castle 的接口代理失效时<b>完全无声</b>：没代理上就没有事务，
/// 业务照常执行、数据照常写入，只是不再原子。这类问题不报错、不记日志，
/// 往往到线上数据对不上才发现 —— 所以凡是能静态判定的失效原因，一律前移到启动期硬报错。
///
/// 归到 "UnitOfWork" 集合：本组直接操作 <see cref="UnitOfWorkDiagnostics"/> 的进程级静态状态。
/// </summary>
[Collection("UnitOfWork")]
public class UnitOfWorkRegistrationTests
{
    #region 被测类型

    public interface IGoodService
    {
        Task<VivApiResult> RunAsync();
    }

    public class GoodService : IGoodService
    {
        [VivUnitOfWork]
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    public class PlainService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    public class NoVirtualService : IGoodService
    {
        [VivUnitOfWork]
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    public class PrivateMethodService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        [VivUnitOfWork]
        private void Secret() { }
    }

    public class StaticMethodService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        [VivUnitOfWork]
        public static void Shared() { }
    }

    public class SyncMethodService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        [VivUnitOfWork]
        public virtual VivApiResult Sync() => VivApiResult.Success();
    }

    public class NonGenericValueTaskService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        [VivUnitOfWork]
        public virtual ValueTask Vt() => ValueTask.CompletedTask;
    }

    public interface IVtService
    {
        ValueTask<VivApiResult> RunAsync();
    }

    /// <summary>ValueTask&lt;T&gt; 走异步链，正常放行（非泛型 ValueTask 才拦）</summary>
    public class GenericValueTaskService : IVtService
    {
        [VivUnitOfWork]
        public virtual ValueTask<VivApiResult> RunAsync() => new(VivApiResult.Success());
    }

    /// <summary>没有接口 —— AsImplementedInterfaces() 注册不出代理</summary>
    public class NoInterfaceService
    {
        [VivUnitOfWork]
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>类级特性：方法级不用逐个标，同步方法只告警</summary>
    [VivUnitOfWork]
    public class ClassLevelService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    public sealed class ConsumerStubEvent : NanaEvent;

    /// <summary>
    /// 消费者子类：Worker 侧的事务由基类 HandleAsync 显式读取类级特性来开，不走接口代理 ——
    /// 所以「没按接口注册」这条校验必须放它过去，否则消费者一标特性应用就起不来。
    /// </summary>
    [VivUnitOfWork]
    public sealed class ConsumerStub : VivConsumer<ConsumerStubEvent>
    {
        public ConsumerStub() : base(null!) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<ConsumerStubEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Success());
    }

    #endregion

    private static Type[] Resolve(
        IEnumerable<Type> registered,
        IEnumerable<Type>? dependencies = null,
        bool databaseEnabled = true)
        => UnitOfWorkRegistration.Resolve(registered, dependencies ?? [], databaseEnabled);

    [Fact]
    public void 方法级特性_进拦截清单()
    {
        var intercepted = Resolve([typeof(GoodService)]);

        Assert.Single(intercepted);
        Assert.Equal(typeof(GoodService), intercepted[0]);
    }

    [Fact]
    public void 没标特性的类型_不进拦截清单()
    {
        Assert.Empty(Resolve([typeof(PlainService)]));
    }

    [Fact]
    public void 非virtual方法带特性_抛且消息指名道姓()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(NoVirtualService)]));

        Assert.Contains("RunAsync", ex.Message);
        Assert.Contains("virtual", ex.Message);
    }

    [Fact]
    public void 隐式实现接口的公开方法_元数据里是virtual但final_拦截不了()
    {
        // 钉住一个实测事实，也是启动期校验最容易写错的地方：
        // C# 会把「隐式实现接口的 public 方法」编译成 virtual + final。
        // 只判 !IsVirtual 会把它当成可拦截 → 校验恰好把最容易写出的那种方法放了过去，
        // 且失效完全无声。必须判 IsVirtual && !IsFinal。
        var implicitImpl = typeof(NoVirtualService).GetMethod(nameof(NoVirtualService.RunAsync))!;
        Assert.True(implicitImpl.IsVirtual, "隐式实现接口的方法在元数据里是 virtual");
        Assert.True(implicitImpl.IsFinal, "但它同时是 sealed —— Castle 重写不了");

        // 显式写了 virtual 的才是真可重写
        Assert.False(typeof(GoodService).GetMethod(nameof(GoodService.RunAsync))!.IsFinal);
    }

    [Fact]
    public void private方法带特性_抛()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(PrivateMethodService)]));

        Assert.Contains("Secret", ex.Message);
    }

    [Fact]
    public void 静态方法带特性_抛()
    {
        // 回归：枚举方法的 BindingFlags 漏了 Static 的话，这条会静默放行
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(StaticMethodService)]));

        Assert.Contains("Shared", ex.Message);
    }

    [Fact]
    public void 同步方法带特性_抛()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(SyncMethodService)]));

        Assert.Contains("Sync", ex.Message);
        Assert.Contains("同步方法", ex.Message);
    }

    [Fact]
    public void 非泛型ValueTask带特性_抛()
    {
        // 实测反直觉：同步方法和非泛型 ValueTask 都走不可重写的同步路径，
        // 表现是「方法照常执行、事务根本没开」；泛型版 ValueTask<T> 才走异步链
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(NonGenericValueTaskService)]));

        Assert.Contains("ValueTask", ex.Message);
    }

    [Fact]
    public void 泛型ValueTask带特性_放行()
    {
        Assert.Single(Resolve([typeof(GenericValueTaskService)]));
    }

    [Fact]
    public void 类型没有任何接口_抛()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(NoInterfaceService)]));

        Assert.Contains("接口", ex.Message);
    }

    [Fact]
    public void 特性标在未按接口注册的类型上_抛()
    {
        // 最隐蔽的一种失效：能编译、能跑、就是没事务（没进 DIOption 扫描 / 标了 AsSelf）
        var ex = Assert.Throws<InvalidOperationException>(
            () => Resolve([typeof(PlainService)], [typeof(NoInterfaceService)]));

        Assert.Contains("按接口注册", ex.Message);
    }

    [Fact]
    public void 没配数据库却标了特性_抛()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Resolve([typeof(GoodService)], databaseEnabled: false));

        Assert.Contains("DatabaseOption", ex.Message);
    }

    [Fact]
    public void 消费者子类_豁免不抛()
    {
        var intercepted = Resolve([typeof(PlainService)], [typeof(ConsumerStub)]);

        Assert.Empty(intercepted);
    }

    [Fact]
    public void 类级特性_同步虚方法不报错_只在启动期告警()
    {
        UnitOfWorkDiagnostics.ResetForTest();

        // 不抛 —— 类级特性覆盖的是接口暴露出去的异步方法，同步方法只是拿不到事务，
        // 拦也拦不住（异步拦截链不处理它），所以降级为启动期告警
        Assert.Single(Resolve([typeof(ClassLevelService)]));

        var logger = new RecordingLogger();
        UnitOfWorkDiagnostics.LogOnce(logger);

        Assert.Contains(logger.Warnings,
            w => w.Contains(nameof(ClassLevelService.SyncVirtual)));
    }

    [Fact]
    public void 启动日志_报告拦截了多少处()
    {
        UnitOfWorkDiagnostics.ResetForTest();

        Resolve([typeof(GoodService)]);

        var logger = new RecordingLogger();
        UnitOfWorkDiagnostics.LogOnce(logger);

        Assert.Contains(logger.Infos, i => i.Contains("工作单元"));
    }
}
