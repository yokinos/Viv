using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Outbox;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox.Tests;

/// <summary>
/// 装配。这里钉的是两个「改一行就静默失效」的决定：
/// 入队器必须是 Scoped（否则入队跑到业务事务外面），投递器必须由开关门控。
/// </summary>
public class OutboxRegisterTests
{
    private static ServiceCollection Register(OutboxOptions? options = null)
    {
        var services = new ServiceCollection();
        OutboxRegister.Initialize(services, options ?? new OutboxOptions());
        return services;
    }

    [Fact]
    public void 入队器与仓储都是Scoped()
    {
        var services = Register();

        // ★ 这条是整个模式的地基：MomoDatabaseContext 的事务状态挂在实例字段上，
        //   Singleton / Transient 都会让入队落到业务事务外面 —— 原子性当场归零，
        //   而且**编译通过、测试通过、只是不原子**，往往到线上数据对不上才发现。
        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(IVivOutbox)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(IOutboxRepository)).Lifetime);
    }

    [Fact]
    public void 类型解析工厂是Singleton()
    {
        var services = Register();

        // 类型索引要扫全部程序集，而且发送器不持作用域内的东西 —— 进程内一份就够
        var descriptor = services.Single(x => x.ServiceType == typeof(IOutboxEnvelopeSenderFactory));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void 投递器开关打开_注册后台服务()
    {
        var services = Register(new OutboxOptions { EnableDispatcher = true });

        Assert.Contains(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void 投递器开关关掉_不注册后台服务()
    {
        var services = Register(new OutboxOptions { EnableDispatcher = false });

        // 「本进程只写不投」的部署形态：消息靠别的服务去发
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void 关掉投递器_入队器照样注册()
    {
        var services = Register(new OutboxOptions { EnableDispatcher = false });

        Assert.Contains(services, x => x.ServiceType == typeof(IVivOutbox));
    }

    [Fact]
    public void 装配不重复注册OutboxOptions()
    {
        var services = Register();

        // OutboxOptions / IOptions<OutboxOptions> 由 VivConfigLoader.AddVivConfig 统一注册。
        // 这里再注册一份会多出一个配置源，热更新下两份值会各走各的。
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(OutboxOptions));
    }
}
