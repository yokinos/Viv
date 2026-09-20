using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Viv.Contracts.Attributes;
using Viv.Contracts.Events;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Engine.LocalEvents;
using Viv.Engine.UnitOfWork;
using Viv.Fakes;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 工作单元拦截器 + 真实本地事件总线的组合语义（零数据库）。
/// 模拟 HTTP 过滤器：成功 Flush，失败 / 异常 Discard。
/// </summary>
[Collection("UnitOfWork")]
public class UnitOfWorkLocalEventComboTests
{
    public UnitOfWorkLocalEventComboTests() => Hits.Clear();

    private static readonly List<string> Hits = [];

    public sealed class ComboEvent : LocalEvent;

    public sealed class ComboHandler : IVivLocalEventHandler<ComboEvent>
    {
        public Task HandleAsync(ComboEvent @event, CancellationToken ct = default)
        {
            Hits.Add("combo");
            return Task.CompletedTask;
        }
    }

    public interface IComboService
    {
        Task<VivApiResult> SuccessAsync();
        Task<VivApiResult> FailureEnvelopeAsync();
        Task<VivApiResult> StickySuccessAsync();
    }

    public class ComboService : IComboService
    {
        private readonly IVivUnitOfWork _unitOfWork;
        private readonly IVivLocalEventBus _bus;

        public ComboService(IVivUnitOfWork unitOfWork, IVivLocalEventBus bus)
        {
            _unitOfWork = unitOfWork;
            _bus = bus;
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> SuccessAsync()
        {
            await _bus.PublishAsync(new ComboEvent());
            return VivApiResult.Success("ok");
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> FailureEnvelopeAsync()
        {
            await _bus.PublishAsync(new ComboEvent());
            return VivApiResult.Failed("业务失败");
        }

        [VivUnitOfWork]
        public virtual async Task<VivApiResult> StickySuccessAsync()
        {
            await _bus.PublishAsync(new ComboEvent());
            await using (var inner = await _unitOfWork.BeginAsync())
            {
            }

            return VivApiResult.Success("粘性成功信封");
        }
    }

    private static (IContainer Container, KernelStub Kernel, LocalEventBus Bus) Build()
    {
        var kernel = new KernelStub();
        var logger = new RecordingLogger();
        var bus = new LocalEventBus([new LocalEventHandlerInvoker<ComboEvent>([new ComboHandler()])], logger);

        var builder = new ContainerBuilder();
        builder.RegisterInstance(kernel).As<ITransactionKernel>().SingleInstance();
        builder.RegisterInstance(logger).As<ILoggerContract>().SingleInstance();
        builder.RegisterInstance(bus).As<IVivLocalEventBus>().SingleInstance();
        builder.RegisterType<UnitOfWorkManager>().As<IVivUnitOfWork>().InstancePerLifetimeScope();
        builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();
        builder.Register(c => new AsyncDeterminationInterceptor(c.Resolve<VivUnitOfWorkInterceptor>()))
            .AsSelf()
            .InstancePerLifetimeScope();
        builder.RegisterType<ComboService>()
            .As<IComboService>()
            .InstancePerLifetimeScope()
            .EnableInterfaceInterceptors()
            .InterceptedBy(typeof(AsyncDeterminationInterceptor));

        return (builder.Build(), kernel, bus);
    }

    /// <summary>与 LocalEventFlushFilterAttribute 同一套：成功 Flush，失败 Discard，异常 Discard 再上抛。</summary>
    private static async Task<VivApiResult?> InvokeLikeFilter(IVivLocalEventBus bus, Func<Task<VivApiResult>> action)
    {
        VivApiResult result;
        try
        {
            result = await action();
        }
        catch
        {
            bus.Discard();
            throw;
        }

        if (FailDetector.IsFailed(result))
        {
            bus.Discard();
            return result;
        }

        await bus.FlushAsync(CancellationToken.None);
        return result;
    }

    [Fact]
    public async Task 成功_先提交再跑handler()
    {
        var (container, kernel, bus) = Build();
        using var scope = container.BeginLifetimeScope();
        kernel.OnCommit = () =>
        {
            Assert.Empty(Hits);
            return Task.CompletedTask;
        };

        var result = await InvokeLikeFilter(bus, () => scope.Resolve<IComboService>().SuccessAsync());

        Assert.Equal(200, result!.Code);
        Assert.Equal("begin|commit", kernel.Trace());
        Assert.Equal("combo", Assert.Single(Hits));
    }

    [Fact]
    public async Task 失败信封_回滚且不分发()
    {
        var (container, kernel, bus) = Build();
        using var scope = container.BeginLifetimeScope();

        var result = await InvokeLikeFilter(bus, () => scope.Resolve<IComboService>().FailureEnvelopeAsync());

        Assert.Equal(-200, result!.Code);
        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Empty(Hits);
    }

    [Fact]
    public async Task 粘性加成功信封_回滚且Discard不分发()
    {
        var (container, kernel, bus) = Build();
        using var scope = container.BeginLifetimeScope();

        await Assert.ThrowsAsync<VivUnitOfWorkException>(
            () => InvokeLikeFilter(bus, () => scope.Resolve<IComboService>().StickySuccessAsync()));

        Assert.Equal("begin|rollback", kernel.Trace());
        Assert.Equal(0, kernel.Count("commit"));
        Assert.Empty(Hits);

        // Discard 之后再 Flush 也捞不出事件
        await bus.FlushAsync();
        Assert.Empty(Hits);
    }
}
