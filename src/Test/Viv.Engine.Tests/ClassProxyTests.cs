using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Viv.Contracts.Attributes;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Engine.Locking;
using Viv.Engine.UnitOfWork;
using Viv.Fakes;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 无业务接口的实现 —— Castle 类代理（<c>AsSelf</c> + <c>EnableClassInterceptors</c>）真跑一遍。
///
/// 与接口代理的测试分开是因为两者行为不同：类代理生成子类，只能拦 public virtual 方法；
/// 而且它会拦 <c>this.VirtualMethod()</c>（虚派发），接口代理不会。
/// 归到 "UnitOfWork" 集合：与工作单元那几组共用 <c>UnitOfWorkDiagnostics</c> 的进程级静态状态。
/// </summary>
[Collection("UnitOfWork")]
public class ClassProxyTests
{
    public ClassProxyTests() => ProbeService.BodyFinished = false;

    #region 被测类型

    /// <summary>无业务接口的 UoW 探针 —— 没接口可注，只能按具体类型解析</summary>
    public class ProbeService
    {
        /// <summary>业务体真正跑完时置位 —— 内核在「提交那一刻」读它</summary>
        public static bool BodyFinished;

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> CreateAsync()
        {
            await Task.Delay(20);
            BodyFinished = true;
            return VivApiResult.Success("创建成功");
        }

        [VivUnitOfWork]
        public virtual Task<VivApiResult> FailingAsync()
            => Task.FromResult(VivApiResult.Failed("业务失败"));

        [VivUnitOfWork]
        public virtual Task<VivApiResult> ThrowingAsync()
            => throw new InvalidOperationException("业务炸了");

        /// <summary>没标特性 —— 原样放行，一次事务都不碰</summary>
        public virtual Task<VivApiResult> UntaggedAsync()
            => Task.FromResult(VivApiResult.Success("没标特性"));

        /// <summary>自调用：外层没标、内层标了 —— 类代理按虚派发会把内层拦到</summary>
        public virtual Task<VivApiResult> OuterAsync() => InnerAsync();

        [VivUnitOfWork]
        public virtual Task<VivApiResult> InnerAsync()
            => Task.FromResult(VivApiResult.Success("内层"));
    }

    /// <summary>无业务接口的锁探针</summary>
    public class LockProbeService
    {
        public static int BodyCalls;

        [VivDistributedLock("order", KeyTemplate = "42")]
        public virtual Task RunAsync()
        {
            BodyCalls++;
            return Task.CompletedTask;
        }

        /// <summary>没标特性 —— 一次锁都不该取</summary>
        public virtual Task UntaggedAsync() => Task.CompletedTask;

        /// <summary>
        /// 自调用：外层与内层用同一个 Key。类代理会把内层也拦到，于是第二次取锁 ——
        /// 真 Redis 上默认不可重入，这一步会抛 DistributedLockException。
        /// 所以类级锁 / 互相调用的锁方法必须配 Reentrant = true，或只标最外层。
        /// </summary>
        [VivDistributedLock("order", KeyTemplate = "same")]
        public virtual Task OuterLockedAsync() => InnerLockedAsync();

        [VivDistributedLock("order", KeyTemplate = "same")]
        public virtual Task InnerLockedAsync() => Task.CompletedTask;
    }

    /// <summary>有业务接口的探针 —— 必须继续走接口代理（非 virtual 也能拦）</summary>
    public interface IInterfaceProbe
    {
        Task<VivApiResult> RunAsync();
    }

    public class InterfaceProbe : IInterfaceProbe
    {
        [VivUnitOfWork]
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success("接口代理"));
    }

    /// <summary>无接口、也没标特性的扫描类型 —— 现在应按 AsSelf 注册，不再「扫描到等于没注册」</summary>
    public class PlainProbe
    {
        public Task<VivApiResult> RunAsync() => Task.FromResult(VivApiResult.Success("普通类"));
    }

    #endregion

    private static (IContainer Container, KernelStub Kernel, RecordingLogger Logger) BuildUnitOfWork()
    {
        var kernel = new KernelStub();
        var logger = new RecordingLogger();

        var builder = new ContainerBuilder();

        builder.RegisterInstance(kernel).As<ITransactionKernel>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();
        builder.RegisterType<UnitOfWorkManager>().As<IVivUnitOfWork>().InstancePerLifetimeScope();
        builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();
        builder.Register(c => new UnitOfWorkInterceptorAdapter(c.Resolve<VivUnitOfWorkInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<ProbeService>()
            .AsSelf()
            .InstancePerLifetimeScope()
            .EnableClassInterceptors()
            .InterceptedBy(typeof(UnitOfWorkInterceptorAdapter));

        return (builder.Build(), kernel, logger);
    }

    private static (IContainer Container, RecordingDistributedLock Lock, RecordingLogger Logger) BuildLock()
    {
        var distributedLock = new RecordingDistributedLock();
        var logger = new RecordingLogger();

        var builder = new ContainerBuilder();

        builder.RegisterInstance(distributedLock).As<IDistributedLock>().SingleInstance();
        builder.RegisterInstance(new TestContext()).As<IVivContext>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();

        builder.RegisterType<VivDistributedLockInterceptor>().AsSelf().InstancePerLifetimeScope();
        builder.Register(c => new DistributedLockInterceptorAdapter(c.Resolve<VivDistributedLockInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<LockProbeService>()
            .AsSelf()
            .InstancePerLifetimeScope()
            .EnableClassInterceptors()
            .InterceptedBy(typeof(DistributedLockInterceptorAdapter));

        return (builder.Build(), distributedLock, logger);
    }

    [Fact]
    public async Task 类代理_提交发生在方法体真正结束之后()
    {
        var (container, kernel, _) = BuildUnitOfWork();
        using var scope = container.BeginLifetimeScope();

        var commitSawBodyFinished = false;
        kernel.OnCommit = () =>
        {
            commitSawBodyFinished = ProbeService.BodyFinished;
            return Task.CompletedTask;
        };

        var result = await scope.Resolve<ProbeService>().CreateAsync();

        Assert.Equal(200, result.Code);
        Assert.True(ProbeService.BodyFinished);
        Assert.True(commitSawBodyFinished, "类代理下提交也必须晚于业务方法体结束");
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 类代理_失败信封回滚()
    {
        var (container, kernel, logger) = BuildUnitOfWork();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<ProbeService>().FailingAsync();

        Assert.Equal(-200, result.Code);
        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Contains(logger.Warnings, w => w.Contains("失败信封"));
    }

    [Fact]
    public async Task 类代理_抛异常回滚后原样上抛()
    {
        var (container, kernel, _) = BuildUnitOfWork();
        using var scope = container.BeginLifetimeScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Resolve<ProbeService>().ThrowingAsync());

        Assert.Equal("业务炸了", ex.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 类代理_未标方法_直接放行()
    {
        var (container, kernel, _) = BuildUnitOfWork();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<ProbeService>().UntaggedAsync();

        Assert.Equal(200, result.Code);
        Assert.Empty(kernel.Calls);
    }

    [Fact]
    public async Task 类代理_自调用虚方法_同样被拦()
    {
        // 这是类代理与接口代理最重要的行为差：this.VirtualMethod() 走虚派发，
        // 会重新进代理 → 内层自己开一层嵌套事务。接口代理下这句调用完全不过代理。
        var (container, kernel, _) = BuildUnitOfWork();
        using var scope = container.BeginLifetimeScope();

        await scope.Resolve<ProbeService>().OuterAsync();

        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task RegisterScannedTypes_无接口类型_按AsSelf注册并挂类代理()
    {
        // 注册路径本身也要钉住：只写 Resolve 校验、忘了 AsSelf / 类代理的话，
        // 无接口类要么解析不到，要么解析到裸实例 —— 两种都是静默无事务。
        var unitOfWork = new RecordingUnitOfWork();
        var logger = new RecordingLogger();

        var builder = new ContainerBuilder();
        builder.RegisterInstance(unitOfWork).As<IVivUnitOfWork>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();
        builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();
        builder.Register(c => new UnitOfWorkInterceptorAdapter(c.Resolve<VivUnitOfWorkInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();

        VivEngineExtensions.RegisterScannedTypes(
            builder,
            [typeof(ProbeService)],
            [typeof(ProbeService)],
            []);

        using var container = builder.Build();
        using var scope = container.BeginLifetimeScope();

        var service = scope.Resolve<ProbeService>();

        Assert.IsNotType<ProbeService>(service);              // 必须是 Castle 生成的子类
        Assert.IsAssignableFrom<ProbeService>(service);

        await service.CreateAsync();
        Assert.Equal("begin|commit", unitOfWork.Trace());
    }

    [Fact]
    public async Task 类代理_取锁后释放()
    {
        var (container, distributedLock, _) = BuildLock();
        using var scope = container.BeginLifetimeScope();

        LockProbeService.BodyCalls = 0;
        await scope.Resolve<LockProbeService>().RunAsync();

        Assert.Equal(1, LockProbeService.BodyCalls);
        Assert.Equal(1, distributedLock.AcquireCalls);
        Assert.Equal(1, distributedLock.ReleaseCalls);
        Assert.Equal(distributedLock.LastLockKey, distributedLock.LastReleaseKey);
    }

    [Fact]
    public async Task 类代理_取锁失败_抛且不执行方法体()
    {
        var (container, distributedLock, _) = BuildLock();
        using var scope = container.BeginLifetimeScope();

        distributedLock.AcquireResult = false;
        LockProbeService.BodyCalls = 0;

        await Assert.ThrowsAsync<DistributedLockException>(
            () => scope.Resolve<LockProbeService>().RunAsync());

        Assert.Equal(0, LockProbeService.BodyCalls);
        Assert.Equal(0, distributedLock.ReleaseCalls);
    }

    [Fact]
    public async Task 类代理_未标方法_不取锁()
    {
        var (container, distributedLock, _) = BuildLock();
        using var scope = container.BeginLifetimeScope();

        await scope.Resolve<LockProbeService>().UntaggedAsync();

        Assert.Equal(0, distributedLock.AcquireCalls);
        Assert.Equal(0, distributedLock.ReleaseCalls);
    }

    [Fact]
    public async Task 类代理_自调用锁方法_会二次取锁()
    {
        // 行为记录，不是推荐用法：类代理把 this.InnerLockedAsync() 也拦到，
        // 同一把 Key 取两次。真实 Redis 锁默认不可重入，第二次会抛 ——
        // 所以类级 [VivDistributedLock] + 方法互调必须显式 Reentrant = true，或只标最外层。
        var (container, distributedLock, _) = BuildLock();
        using var scope = container.BeginLifetimeScope();

        await scope.Resolve<LockProbeService>().OuterLockedAsync();

        Assert.Equal(2, distributedLock.AcquireCalls);
        Assert.Equal(2, distributedLock.ReleaseCalls);
    }

    [Fact]
    public async Task RegisterScannedTypes_有接口类型_仍走接口代理_非virtual也能拦()
    {
        // 双路径回归：有业务接口的类型不能被误改成类代理 —— 它的 RunAsync 是非 virtual
        //（隐式实现接口编译成 virtual final），类代理根本拦不到。
        var unitOfWork = new RecordingUnitOfWork();
        var logger = new RecordingLogger();

        var builder = new ContainerBuilder();
        builder.RegisterInstance(unitOfWork).As<IVivUnitOfWork>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();
        builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();
        builder.Register(c => new UnitOfWorkInterceptorAdapter(c.Resolve<VivUnitOfWorkInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();

        VivEngineExtensions.RegisterScannedTypes(
            builder,
            [typeof(InterfaceProbe)],
            [typeof(InterfaceProbe)],
            []);

        using var container = builder.Build();
        using var scope = container.BeginLifetimeScope();

        var service = scope.Resolve<IInterfaceProbe>();

        Assert.IsNotType<InterfaceProbe>(service);            // Castle 接口代理，不是裸实例
        await service.RunAsync();
        Assert.Equal("begin|commit", unitOfWork.Trace());
    }

    [Fact]
    public void RegisterScannedTypes_无接口普通类_按AsSelf注册()
    {
        // 以前 AsImplementedInterfaces() 对无接口类注册不出任何服务 —— 扫描到等于没注册。
        var builder = new ContainerBuilder();

        VivEngineExtensions.RegisterScannedTypes(builder, [typeof(PlainProbe)], [], []);

        using var container = builder.Build();
        using var scope = container.BeginLifetimeScope();

        Assert.IsType<PlainProbe>(scope.Resolve<PlainProbe>());
    }
}
