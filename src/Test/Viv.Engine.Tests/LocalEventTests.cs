using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Engine.LocalEvents;
using Viv.Fakes;
using Viv.Log;
using Viv.Nana;

namespace Viv.Engine.Tests;

/// <summary>
/// 本地事件总线（IVivLocalEventBus）—— 状态机、分发语义、扫描注册。
/// </summary>
public class LocalEventTests
{
    public LocalEventTests() => Hits.Clear();

    /// <summary>分发记录汇（处理器由 DI 无参构造，只能走静态汇；xUnit 同类内串行，无并发问题）</summary>
    private static readonly List<string> Hits = [];

    #region 测试事件与处理器

    public sealed class OrderCreated : LocalEvent;
    public sealed class StockChanged : LocalEvent;
    public sealed class ThrowingEvent : LocalEvent;
    public sealed class RepublishEvent : LocalEvent;
    public sealed class RecursiveEvent : LocalEvent;
    public sealed class NoHandlerEvent : LocalEvent;

    public sealed class OrderCreatedHandler : IVivLocalEventHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated @event, CancellationToken ct = default)
        {
            Hits.Add("order-1");
            return Task.CompletedTask;
        }
    }

    public sealed class OrderCreatedHandler2 : IVivLocalEventHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated @event, CancellationToken ct = default)
        {
            Hits.Add("order-2");
            return Task.CompletedTask;
        }
    }

    public sealed class StockChangedHandler : IVivLocalEventHandler<StockChanged>
    {
        public Task HandleAsync(StockChanged @event, CancellationToken ct = default)
        {
            Hits.Add("stock");
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingHandler : IVivLocalEventHandler<ThrowingEvent>
    {
        public Task HandleAsync(ThrowingEvent @event, CancellationToken ct = default)
            => throw new InvalidOperationException("处理器炸了");
    }

    /// <summary>处理器内递归发布：新事件应在同一轮 Flush 内被分发</summary>
    public sealed class RepublishHandler : IVivLocalEventHandler<RepublishEvent>
    {
        public static IVivLocalEventBus? Bus;

        public async Task HandleAsync(RepublishEvent @event, CancellationToken ct = default)
        {
            Hits.Add("republish");
            if (Bus != null)
                await Bus.PublishAsync(new StockChanged(), ct);
        }
    }

    /// <summary>自递归发布：用于验证轮数上限</summary>
    public sealed class RecursiveHandler : IVivLocalEventHandler<RecursiveEvent>
    {
        public static IVivLocalEventBus? Bus;

        public async Task HandleAsync(RecursiveEvent @event, CancellationToken ct = default)
        {
            Hits.Add("recursive");
            if (Bus != null)
                await Bus.PublishAsync(new RecursiveEvent(), ct);
        }
    }

    #endregion

    #region 扫描目标（DI 注册测试用）

    public sealed class ScannedEvent : LocalEvent;
    public sealed class EventA : LocalEvent;
    public sealed class EventB : LocalEvent;

    /// <summary>基类写法</summary>
    public sealed class ScanBaseClassHandler : LocalEventHandler<ScannedEvent>
    {
        public override Task HandleAsync(ScannedEvent @event, CancellationToken ct = default)
        {
            Hits.Add("scan-base");
            return Task.CompletedTask;
        }
    }

    /// <summary>接口写法</summary>
    public sealed class ScanInterfaceHandler : IVivLocalEventHandler<ScannedEvent>
    {
        public Task HandleAsync(ScannedEvent @event, CancellationToken ct = default)
        {
            Hits.Add("scan-interface");
            return Task.CompletedTask;
        }
    }

    /// <summary>同一处理器订阅多个事件 —— 回归防护：注册时必须遍历全部闭合接口，不能只取一个</summary>
    public sealed class MultiEventHandler : IVivLocalEventHandler<EventA>, IVivLocalEventHandler<EventB>
    {
        public Task HandleAsync(EventA @event, CancellationToken ct = default)
        {
            Hits.Add("multi-a");
            return Task.CompletedTask;
        }

        public Task HandleAsync(EventB @event, CancellationToken ct = default)
        {
            Hits.Add("multi-b");
            return Task.CompletedTask;
        }
    }

    #endregion

    #region 辅助

    private static LocalEventBus NewBus(ILoggerContract logger, params ILocalEventHandlerInvoker[] invokers)
        => new(invokers, logger);

    private static LocalEventHandlerInvoker<TEvent> InvokerFor<TEvent>(params IVivLocalEventHandler<TEvent>[] handlers)
        where TEvent : LocalEvent
        => new(handlers);

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerContract, RecordingLogger>();
        LocalEventRegistration.Register(services);
        return services.BuildServiceProvider();
    }

    #endregion

    [Fact]
    public async Task 发布只入队_未Flush不执行()
    {
        var bus = NewBus(new RecordingLogger(), InvokerFor<OrderCreated>(new OrderCreatedHandler()));

        await bus.PublishAsync(new OrderCreated());

        Assert.Empty(Hits);
    }

    [Fact]
    public async Task Flush后执行全部处理器()
    {
        var bus = NewBus(new RecordingLogger(),
            InvokerFor<OrderCreated>(new OrderCreatedHandler(), new OrderCreatedHandler2()),
            InvokerFor<StockChanged>(new StockChangedHandler()));

        await bus.PublishAsync(new OrderCreated());
        await bus.PublishAsync(new StockChanged());
        await bus.FlushAsync();

        // 顺序敏感：按发布顺序、同一事件按处理器注册顺序
        Assert.Equal("order-1|order-2|stock", string.Join("|", Hits));
    }

    [Fact]
    public async Task 处理器抛异常_异常上抛()
    {
        var bus = NewBus(new RecordingLogger(), InvokerFor<ThrowingEvent>(new ThrowingHandler()));

        await bus.PublishAsync(new ThrowingEvent());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bus.FlushAsync());
        Assert.Equal("处理器炸了", ex.Message);
    }

    [Fact]
    public async Task Discard后不再执行()
    {
        var bus = NewBus(new RecordingLogger(), InvokerFor<OrderCreated>(new OrderCreatedHandler()));

        await bus.PublishAsync(new OrderCreated());
        bus.Discard();
        await bus.FlushAsync();

        Assert.Empty(Hits);
    }

    [Fact]
    public async Task 重复Flush只执行一次()
    {
        var log = new RecordingLogger();
        var bus = NewBus(log, InvokerFor<OrderCreated>(new OrderCreatedHandler()));

        await bus.PublishAsync(new OrderCreated());
        await bus.FlushAsync();
        await bus.FlushAsync();

        Assert.Equal("order-1", string.Join("|", Hits));

        // 分发结束后再发布：丢弃 + 记警告，不静默吞
        await bus.PublishAsync(new OrderCreated());
        await bus.FlushAsync();

        Assert.Equal("order-1", string.Join("|", Hits));
        Assert.Contains(log.Warnings, w => w.Contains("分发结束后发布"));
    }

    [Fact]
    public async Task 处理器内再发布_同轮分发()
    {
        var bus = NewBus(new RecordingLogger(),
            InvokerFor<RepublishEvent>(new RepublishHandler()),
            InvokerFor<StockChanged>(new StockChangedHandler()));

        RepublishHandler.Bus = bus;
        try
        {
            await bus.PublishAsync(new RepublishEvent());
            await bus.FlushAsync();
        }
        finally
        {
            RepublishHandler.Bus = null;
        }

        // 处理器的 next() 段（Draining 态）发布的 StockChanged 必须被同一轮 Flush 捞出
        Assert.Equal("republish|stock", string.Join("|", Hits));
    }

    [Fact]
    public async Task 递归发布超过上限_记录错误并停止()
    {
        var log = new RecordingLogger();
        var bus = NewBus(log, InvokerFor<RecursiveEvent>(new RecursiveHandler()));

        RecursiveHandler.Bus = bus;
        try
        {
            await bus.PublishAsync(new RecursiveEvent());
            await bus.FlushAsync();
        }
        finally
        {
            RecursiveHandler.Bus = null;
        }

        Assert.Equal(5, Hits.Count);
        Assert.Contains(log.Errors, e => e.Contains("递归发布"));
    }

    [Fact]
    public async Task 无处理器的事件_跳过不抛()
    {
        var log = new RecordingLogger();
        var bus = NewBus(log);

        await bus.PublishAsync(new NoHandlerEvent());
        await bus.FlushAsync();

        Assert.Contains(log.Warnings, w => w.Contains("无处理器"));
    }

    [Fact]
    public async Task 未Flush作用域结束_Dispose记录警告()
    {
        var log = new RecordingLogger();
        var bus = NewBus(log, InvokerFor<OrderCreated>(new OrderCreatedHandler()));

        await bus.PublishAsync(new OrderCreated());
        bus.Dispose();

        Assert.Empty(Hits);
        Assert.Contains(log.Warnings, w => w.Contains("未分发"));
    }

    [Fact]
    public async Task 基类写法与接口写法_都能被扫到并分发()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<IVivLocalEventBus>();
        await bus.PublishAsync(new ScannedEvent());
        await bus.FlushAsync();

        Assert.Contains("scan-base", Hits);
        Assert.Contains("scan-interface", Hits);
    }

    [Fact]
    public async Task 同一处理器订阅多个事件_两个都分发()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<IVivLocalEventBus>();
        await bus.PublishAsync(new EventA());
        await bus.PublishAsync(new EventB());
        await bus.FlushAsync();

        Assert.Contains("multi-a", Hits);
        Assert.Contains("multi-b", Hits);
    }

    [Fact]
    public void LocalEvent是空标记基类_且与NanaEvent互不继承()
    {
        const System.Reflection.BindingFlags declared =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;

        // 纯限制：抽象 + 自身零成员
        Assert.True(typeof(LocalEvent).IsAbstract);
        Assert.Equal(typeof(object), typeof(LocalEvent).BaseType);
        Assert.Empty(typeof(LocalEvent).GetFields(declared));
        Assert.Empty(typeof(LocalEvent).GetProperties(declared));
        Assert.Empty(typeof(LocalEvent).GetMethods(declared));

        // 关键回归：一旦 LocalEvent 继承 NanaEvent，Wolverine 会给所有本地事件注册 RabbitMQ 路由
        //（VivWolverineConfigurationExtensions 扫 NanaEvent 子类），本地事件就被绑死成跨进程语义了
        Assert.False(typeof(NanaEvent).IsAssignableFrom(typeof(LocalEvent)));
    }

    [Fact]
    public void 开放泛型扫描_能找到两种写法的处理器()
    {
        TypeScanMagic.ForceLoadReferencedAssemblies();

        var found = TypeScanMagic.ScanTypes(typeof(IVivLocalEventHandler<>));

        // 接口写法直接命中，基类写法经其实现的接口命中（扫描目标统一是接口）
        Assert.Contains(typeof(ScanInterfaceHandler), found);
        Assert.Contains(typeof(ScanBaseClassHandler), found);
    }
}
