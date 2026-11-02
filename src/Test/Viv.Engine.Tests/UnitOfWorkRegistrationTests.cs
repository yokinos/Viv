using Viv.Contracts.Attributes;
using Viv.Engine.UnitOfWork;
using Viv.Fakes;
using Viv.Nana;

namespace Viv.Engine.Tests;

/// <summary>
/// 工作单元的注册期筛选与启动期校验。
///
/// 校验之所以这么重：Castle 的接口代理失效时完全无声，没代理上就没有事务，
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

        /// <summary>ValueTask&lt;T&gt; 不能安全拦截，注册期必须拒绝（见 UnitOfWorkRegistrationTests）</summary>
        public class GenericValueTaskService : IVtService
    {
        [VivUnitOfWork]
        public virtual ValueTask<VivApiResult> RunAsync() => new(VivApiResult.Success());
    }

    /// <summary>没有业务接口 + public virtual —— 走类代理，可以拦截</summary>
    public class NoInterfaceService
    {
        [VivUnitOfWork]
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>没有业务接口 + 非 virtual —— 类代理拦不到（接口代理的那套结论不适用）</summary>
    public class NoInterfaceNonVirtualService
    {
        [VivUnitOfWork]
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>没有业务接口 + sealed —— Castle 生成不出子类</summary>
    public sealed class SealedNoInterfaceService
    {
        [VivUnitOfWork]
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>没有业务接口 + internal —— 动态程序集继承不了（本仓没有 DynamicProxyGenAssembly2 的 IVT）</summary>
    internal class InternalNoInterfaceService
    {
        [VivUnitOfWork]
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>没有业务接口 + 类级特性 + 非 virtual 方法 —— 类级特性下同样必须能拦到</summary>
    [VivUnitOfWork]
    public class ClassLevelNoInterfaceService
    {
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>没有业务接口、也没标特性 —— 不进拦截清单，更不该抛</summary>
    public class PlainNoInterfaceService
    {
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>只实现 IDisposable —— 不算有业务接口，应该走类代理</summary>
    public class DisposableOnlyService : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>类级特性：方法级不用逐个标；拦不到的方法（如同步方法）启动失败</summary>
    [VivUnitOfWork]
    public class ClassLevelService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>类级 + 隐式接口实现（virtual+final）—— 接口代理照样拦得到</summary>
    [VivUnitOfWork]
    public class ClassLevelImplicitService : IGoodService
    {
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>类级 + 同步虚方法 —— 必须启动失败</summary>
    [VivUnitOfWork]
    public class ClassLevelSyncService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    public interface IOpenGenericService<T>
    {
        Task<VivApiResult> RunAsync();
    }

    [VivUnitOfWork]
    public class ClassLevelOptOutSyncService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        [VivUnitOfWork(Enabled = false)]
        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    /// <summary>类级 Enabled = false：整个类豁免，与方法级 opt-out 同一待遇</summary>
    [VivUnitOfWork(Enabled = false)]
    public class ClassLevelOptOutService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());

        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    /// <summary>基类标类级特性，供派生类的用例继承（AttributeUsage 是 Inherited = true）</summary>
    [VivUnitOfWork]
    public class ClassLevelBaseService : IGoodService
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    /// <summary>派生类继承了基类的类级特性，自己又加了个同步方法 —— 同样该失败</summary>
    public class ClassLevelDerivedService : ClassLevelBaseService
    {
        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    /// <summary>派生类去掉继承来的类级特性只有标 Enabled = false 这一条路，必须真的豁免</summary>
    [VivUnitOfWork(Enabled = false)]
    public class ClassLevelDerivedOptOutService : ClassLevelBaseService
    {
        public virtual VivApiResult SyncVirtual() => VivApiResult.Success();
    }

    [VivUnitOfWork]
    public class OpenGenericAttributedService<T> : IOpenGenericService<T>
    {
        public virtual Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success());
    }

    public sealed class ConsumerStubEvent : NanaEvent;

    /// <summary>
    /// 消费者子类：HandleAsync 按特性显式开事务，不走接口代理 ——
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
    public void 非virtual方法带特性_可以拦截()
    {
        // 接口代理按接口调用、反射转给目标实例，不要求实现方法 virtual
        var intercepted = Resolve([typeof(NoVirtualService)]);

        Assert.Contains(typeof(NoVirtualService), intercepted);
    }

    [Fact]
    public void 隐式实现接口的公开方法_元数据是virtual_final_但接口代理仍能拦()
    {
        // C# 会把「隐式实现接口的 public 方法」编译成 virtual + final。
        // 接口代理（CreateInterfaceProxyWithTarget）按接口调用、反射转给目标实例，不依赖虚方法表，
        // 所以 IsFinal 不影响拦截 —— 别把 IsVirtual / IsFinal 再当成注册条件加回来。
        var implicitImpl = typeof(NoVirtualService).GetMethod(nameof(NoVirtualService.RunAsync))!;
        Assert.True(implicitImpl.IsVirtual, "隐式实现接口的方法在元数据里是 virtual");
        Assert.True(implicitImpl.IsFinal, "它同时是 final");

        Assert.Contains(typeof(NoVirtualService), Resolve([typeof(NoVirtualService)]));
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
        // 同步方法、非泛型 ValueTask、ValueTask<T> 都不能安全拦截，启动期一律拒绝。
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(NonGenericValueTaskService)]));

        Assert.Contains("ValueTask", ex.Message);
    }

    [Fact]
    public void 泛型ValueTask带特性_启动失败()
    {
        // Castle 会进异步链，但提交发生在方法体结束之前 —— 比「不开事务」更毒。
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(GenericValueTaskService)]));

        Assert.Contains("ValueTask", ex.Message);
    }

    [Fact]
    public void 类型没有接口_virtual方法_走类代理_进拦截清单()
    {
        // 无业务接口不再一律拒绝：RegisterScannedTypes 会按 AsSelf + EnableClassInterceptors
        // 挂类代理，被拦方法 public virtual 是硬前提。
        var intercepted = Resolve([typeof(NoInterfaceService)]);

        Assert.Contains(typeof(NoInterfaceService), intercepted);
    }

    [Fact]
    public void 类型没有接口_非virtual方法_启动失败()
    {
        // 接口代理不要求 virtual，类代理必须 —— 这条是两条路径最大的差别。
        // 放过去就是「能编译、能跑、就是没事务」，所以注册期硬报错。
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(NoInterfaceNonVirtualService)]));

        Assert.Contains(nameof(NoInterfaceNonVirtualService.RunAsync), ex.Message);
        Assert.Contains("virtual", ex.Message);
    }

    [Fact]
    public void 类型没有接口_sealed_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(SealedNoInterfaceService)]));

        Assert.Contains("sealed", ex.Message);
    }

    [Fact]
    public void 类型没有接口_非public_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(InternalNoInterfaceService)]));

        Assert.Contains("public", ex.Message);
    }

    [Fact]
    public void 类型没有接口_类级特性含非virtual方法_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(ClassLevelNoInterfaceService)]));

        Assert.Contains(nameof(ClassLevelNoInterfaceService.RunAsync), ex.Message);
        Assert.Contains("virtual", ex.Message);
    }

    [Fact]
    public void 类型没有接口_没标特性_不进拦截清单也不抛()
    {
        Assert.Empty(Resolve([typeof(PlainNoInterfaceService)]));
    }

    [Fact]
    public void 只实现IDisposable_不算有业务接口()
    {
        // 只实现 IDisposable 的类如果算「有接口」，AsImplementedInterfaces() 会把它注册成 IDisposable，
        // 按具体类型反而解析不到 —— 所以业务接口判定要排除生命周期 / 标记接口。
        Assert.True(ProxyPolicy.NeedsClassProxy(typeof(DisposableOnlyService)));
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
    public void 类级特性_可重写异步方法_进拦截清单()
    {
        Assert.Single(Resolve([typeof(ClassLevelService)]));
    }

    [Fact]
    public void 类级特性_隐式接口实现virtual加final_也能拦截()
    {
        Assert.Single(Resolve([typeof(ClassLevelImplicitService)]));
    }

    [Fact]
    public void 类级特性_同步虚方法_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(ClassLevelSyncService)]));

        Assert.Contains(nameof(ClassLevelSyncService.SyncVirtual), ex.Message);
        Assert.Contains("同步方法", ex.Message);
    }

    [Fact]
    public void 类级特性_方法级Enabled为false的同步方法_跳过校验()
    {
        Assert.Single(Resolve([typeof(ClassLevelOptOutSyncService)]));
    }

    [Fact]
    public void 类级特性_Enabled为false_整个类豁免校验()
    {
        Assert.Single(Resolve([typeof(ClassLevelOptOutService)]));
    }

    [Fact]
    public void 类级特性_派生类继承了基类特性_自己的同步方法同样启动失败()
    {
        // 派生类拿到的类是基类标的那条特性（AttributeUsage Inherited = true），
        // 运行时的 ShouldIntercept 也沿基类链找 —— 注册期只看自身的话这里会静默无事务
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve([typeof(ClassLevelDerivedService)]));

        Assert.Contains(nameof(ClassLevelDerivedService.SyncVirtual), ex.Message);
        Assert.Contains("同步方法", ex.Message);
    }

    [Fact]
    public void 类级特性_派生类标Enabled为false_去掉继承来的特性()
    {
        // 派生类没有别的办法去掉基类继承下来的特性，这条豁免不生效的话「显式关掉反而起不来」
        Assert.Single(Resolve([typeof(ClassLevelDerivedOptOutService)]));
    }

    [Fact]
    public void 开放泛型类型带特性_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Resolve([typeof(OpenGenericAttributedService<>)]));

        Assert.Contains("开放泛型", ex.Message);
        Assert.Contains("OpenGenericAttributedService", ex.Message);
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
