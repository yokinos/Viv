using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Engine.UnitOfWork;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// <c>[VivUnitOfWork]</c> 拦截器 —— 跑<b>真链路</b>：
/// Autofac 容器 → Castle 接口代理 → AsyncDeterminationInterceptor → VivUnitOfWorkInterceptor
/// → UnitOfWorkManager → 内核桩。只把最底下那一层换成桩，上面全是生产代码。
///
/// 【为什么不直接 new 一个拦截器手搓 IInvocation】
/// 那样验不到「接口代理到底有没有把异步调用送到异步重载上」——
/// 而这恰恰是本特性最容易静默失效的地方（Castle 同步 Proceed 在第一个 await 处就返回）。
/// 归到 "UnitOfWork" 集合：与另两组共用 <see cref="UnitOfWorkDiagnostics"/> 的进程级一次性静态状态。
/// </summary>
[Collection("UnitOfWork")]
public class UnitOfWorkInterceptorTests
{
    public UnitOfWorkInterceptorTests() => ProbeService.BodyFinished = false;

    #region 被测类型

    public interface IProbeService
    {
        Task<VivApiResult> CreateAsync();
        Task<VivApiResult> FailingAsync();
        Task<VivApiResult> AcceptedAsync();
        Task<VivApiResult> ThrowingAsync();
        Task<VivApiResult> UntaggedAsync();
        Task<VivApiResult> DisabledAsync();
        Task VoidAsync();
    }

    public class ProbeService : IProbeService
    {
        /// <summary>★ 业务体真正跑完时置位 —— 内核在「提交那一刻」读它</summary>
        public static bool BodyFinished;

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> CreateAsync()
        {
            await Task.Delay(30);
            BodyFinished = true;
            return VivApiResult.Success("创建成功");
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> FailingAsync()
        {
            await Task.Delay(10);
            return VivApiResult.Failed("业务失败");
        }

        [VivUnitOfWork]
        public virtual Task<VivApiResult> AcceptedAsync()
            => Task.FromResult(VivApiResult.ApiResult(ApiResultCode.Accepted, "已受理"));

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> ThrowingAsync()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("业务炸了");
        }

        /// <summary>没标特性 —— 必须原样放行，一次事务都不碰</summary>
        public virtual Task<VivApiResult> UntaggedAsync()
            => Task.FromResult(VivApiResult.Success("没标特性"));

        [VivUnitOfWork(Enabled = false)]
        public virtual Task<VivApiResult> DisabledAsync()
            => Task.FromResult(VivApiResult.Success("明确关掉"));

        /// <summary>无返回值：判不了信封，只看有没有抛异常</summary>
        [VivUnitOfWork]
        public virtual async Task VoidAsync()
        {
            await Task.Delay(10);
            BodyFinished = true;
        }
    }

    public interface IClassLevelService
    {
        Task<VivApiResult> TaggedAsync();
        Task<VivApiResult> OptOutAsync();
    }

    /// <summary>类级特性 —— Worker 侧那条路（下一轮）就先按这个形状走</summary>
    [VivUnitOfWork]
    public class ClassLevelService : IClassLevelService
    {
        public virtual Task<VivApiResult> TaggedAsync()
            => Task.FromResult(VivApiResult.Success("类级生效"));

        [VivUnitOfWork(Enabled = false)]
        public virtual Task<VivApiResult> OptOutAsync()
            => Task.FromResult(VivApiResult.Success("方法级关掉"));
    }

    #endregion

    private static (IContainer Container, KernelStub Kernel, RecordingLogger Logger) Build()
    {
        var kernel = new KernelStub();
        var logger = new RecordingLogger();

        var builder = new ContainerBuilder();

        builder.RegisterInstance(kernel).As<ITransactionKernel>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();
        builder.RegisterType<UnitOfWorkManager>().As<IVivUnitOfWork>().InstancePerLifetimeScope();

        // Autofac 的接口代理从「当前」作用域解析拦截器。断言在 两个作用域_拦截器实例隔离 里钉着。
        builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();

        // InterceptedBy 只认 IInterceptor，而拦截器实现的是 IAsyncInterceptor ——
        // 中间必须垫一层 AsyncDeterminationInterceptor 适配器，否则解析代理时抛 InvalidCastException。
        builder.Register(c => new AsyncDeterminationInterceptor(c.Resolve<VivUnitOfWorkInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<ProbeService>()
            .As<IProbeService>()
            .InstancePerLifetimeScope()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(AsyncDeterminationInterceptor));

        builder.RegisterType<ClassLevelService>()
            .As<IClassLevelService>()
            .InstancePerLifetimeScope()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(AsyncDeterminationInterceptor));

        return (builder.Build(), kernel, logger);
    }

    /// <summary>
    /// ★ 本次最关键的一条 ★
    /// 业务方法体内有 await，提交必须发生在方法体<b>真正结束之后</b>。
    ///
    /// 这条测的就是裸 <c>IInterceptor</c> 会挂的那个点：<c>invocation.Proceed()</c> 在业务遇到
    /// 第一个 await 时就返回了，天真的 <c>Proceed(); Commit();</c> 会在业务跑完之前提交 ——
    /// 事务边界完全错位，而且不报错。Castle.Core.AsyncInterceptor 的 AsyncInterceptorBase
    /// 才让「提交」挂在了业务 Task 真正完成之后。
    /// </summary>
    [Fact]
    public async Task 业务方法内有await_提交发生在方法真正结束之后()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var commitSawBodyFinished = false;
        kernel.OnCommit = () =>
        {
            commitSawBodyFinished = ProbeService.BodyFinished;
            return Task.CompletedTask;
        };

        var service = scope.Resolve<IProbeService>();
        var result = await service.CreateAsync();

        Assert.True(ProbeService.BodyFinished, "业务方法体应当已经跑完");
        Assert.True(commitSawBodyFinished,
            "提交时业务方法体尚未结束 —— 事务边界错位（裸 IInterceptor 的 Proceed() 在第一个 await 处就返回了）");
        Assert.Equal(200, result.Code);
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 返回成功信封_提交()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<IProbeService>().CreateAsync();

        Assert.True(result.Code == 200);
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 返回失败信封_回滚_但返回值原样交回业务()
    {
        var (container, kernel, logger) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<IProbeService>().FailingAsync();

        // HTTP 仍是 200，成败只能看信封码 —— 拦截器判到非 2xx 就回滚
        Assert.Equal(-200, result.Code);
        Assert.Equal("业务失败", result.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Contains(logger.Warnings, w => w.Contains("失败信封"));
    }

    [Fact]
    public async Task 返回201_提交_2xx区间都算成功()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<IProbeService>().AcceptedAsync();

        // 与 LocalEventFlushFilterAttribute 同一套判定：2xx 区间，不是只认 Success=200
        Assert.Equal(201, result.Code);
        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 抛异常_回滚后原样上抛()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Resolve<IProbeService>().ThrowingAsync());

        // 异常绝不能被吞或换类型 —— 上层过滤器要靠它映射错误码
        Assert.Equal("业务炸了", ex.Message);
        Assert.Equal("begin|rollback", kernel.Trace());
    }

    [Fact]
    public async Task 无特性的方法_直接放行_一次事务都不碰()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<IProbeService>().UntaggedAsync();

        Assert.Equal(200, result.Code);
        Assert.Empty(kernel.Calls);
    }

    [Fact]
    public async Task Enabled为false_直接放行()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await scope.Resolve<IProbeService>().DisabledAsync();

        Assert.Equal(200, result.Code);
        Assert.Empty(kernel.Calls);
    }

    [Fact]
    public async Task 无返回值的Task_正常提交()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();

        await scope.Resolve<IProbeService>().VoidAsync();

        Assert.Equal("begin|commit", kernel.Trace());
    }

    [Fact]
    public async Task 类级特性_方法默认开_方法级Enabled为false则放行()
    {
        var (container, kernel, _) = Build();
        using var scope = container.BeginLifetimeScope();
        var service = scope.Resolve<IClassLevelService>();

        await service.TaggedAsync();
        Assert.Equal("begin|commit", kernel.Trace());

        kernel.Calls.Clear();

        // 方法级特性只写了 Enabled=false，Module/Operation 之类的默认值不参与判断 —— 直接放行
        await service.OptOutAsync();
        Assert.Empty(kernel.Calls);
    }

    [Fact]
    public void 两个作用域_拦截器与工作单元实例隔离()
    {
        // ★ 回归护栏：拦截器一旦被 Autofac 解析到根作用域，IVivUnitOfWork 就成了全局单例 ——
        //   并发请求共用同一个事务状态机，一个请求提交会把另一个请求的事务也提交掉。
        var (container, _, _) = Build();
        using var c1 = container.BeginLifetimeScope();
        using var c2 = container.BeginLifetimeScope();

        Assert.NotSame(c1.Resolve<VivUnitOfWorkInterceptor>(), c2.Resolve<VivUnitOfWorkInterceptor>());
        Assert.NotSame(c1.Resolve<IVivUnitOfWork>(), c2.Resolve<IVivUnitOfWork>());
    }
}
