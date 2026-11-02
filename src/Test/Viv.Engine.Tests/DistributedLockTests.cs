using Viv.Contracts.Attributes;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Engine.Locking;

namespace Viv.Engine.Tests;

public class DistributedLockTests
{
    [Fact]
    public void 模板按参数_属性路径_上下文解析()
    {
        var method = typeof(SampleService).GetMethod(nameof(SampleService.PayAsync))!;
        var request = new PayRequest { OrderId = 1001 };

        var key = LockKeyTemplate.Resolve(
            "{tenantId}:{request.OrderId}:{orderId}",
            method.GetParameters(),
            [42L, request],
            tenantId: 9,
            userId: 7,
            appId: 1);

        Assert.Equal("9:1001:42", key);
    }

    [Fact]
    public void 模板占位符对不上参数_启动失败()
    {
        var method = typeof(SampleService).GetMethod(nameof(SampleService.PayAsync))!;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LockKeyTemplate.Validate("{missing}", method.GetParameters(), "Sample.PayAsync"));

        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void 占位符解析为空_直接抛()
    {
        var method = typeof(SampleService).GetMethod(nameof(SampleService.PayAsync))!;
        var request = new PayRequest { OrderId = 1001, Note = null };

        Assert.Throws<DistributedLockKeyException>(() =>
            LockKeyTemplate.Resolve(
                "{request.Note}",
                method.GetParameters(),
                [42L, request],
                tenantId: 9,
                userId: 7,
                appId: 1));
    }

    [Fact]
    public void 没声明Key的方式_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(NoKeyService)], [], lockEnabled: true));

        Assert.Contains("没有可用的锁 Key", ex.Message);
    }

    [Fact]
    public void 唯一ILockKey参数_可以注册()
    {
        var types = DistributedLockRegistration.Resolve([typeof(LockKeyService)], [], lockEnabled: true);

        Assert.Contains(typeof(LockKeyService), types);
    }

    [Fact]
    public void Global和KeyTemplate冲突_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(ConflictService)], [], lockEnabled: true));

        Assert.Contains("二选一", ex.Message);
    }

    [Fact]
    public void 非virtual方法_也可以注册()
    {
        // 接口代理按接口调用、反射转给目标实例，非 virtual 实现同样能拦到。
        // 无业务接口的类代理不适用这条结论 —— 见下面三条。
        var types = DistributedLockRegistration.Resolve([typeof(NonVirtualService)], [], lockEnabled: true);

        Assert.Contains(typeof(NonVirtualService), types);
    }

    [Fact]
    public void 没有接口_virtual方法_走类代理_进拦截清单()
    {
        var types = DistributedLockRegistration.Resolve([typeof(NoInterfaceLockService)], [], lockEnabled: true);

        Assert.Contains(typeof(NoInterfaceLockService), types);
    }

    [Fact]
    public void 没有接口_非virtual方法_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(NoInterfaceNonVirtualLockService)], [], lockEnabled: true));

        Assert.Contains(nameof(NoInterfaceNonVirtualLockService.DoAsync), ex.Message);
        Assert.Contains("virtual", ex.Message);
    }

    [Fact]
    public void 没有接口_sealed_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(SealedNoInterfaceLockService)], [], lockEnabled: true));

        Assert.Contains("sealed", ex.Message);
    }

    [Fact]
    public void 没有接口_非public_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(InternalNoInterfaceLockService)], [], lockEnabled: true));

        Assert.Contains("public", ex.Message);
    }

    [Fact]
    public void 没有接口_类级特性含非virtual方法_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(ClassLevelNoInterfaceLockService)], [], lockEnabled: true));

        Assert.Contains(nameof(ClassLevelNoInterfaceLockService.DoAsync), ex.Message);
        Assert.Contains("virtual", ex.Message);
    }

    [Fact]
    public void 默认失败文案与不可重入()
    {
        var attribute = new VivDistributedLockAttribute("test");

        Assert.Equal("请勿重复提交，请稍等", attribute.Message);
        Assert.False(attribute.Reentrant);
    }

    [Fact]
    public void 自定义失败文案_带在异常上()
    {
        var ex = new DistributedLockException("稍后再试", "lock:aop:test:1", 3);

        Assert.Equal("稍后再试", ex.Message);
        Assert.Equal("lock:aop:test:1", ex.LockKey);
        Assert.Equal(3, ex.RetryCount);
    }

    public interface ISample
    {
        Task PayAsync(long orderId, PayRequest request);
    }

    public interface INoKey
    {
        Task DoAsync(long id);
    }

    public interface ILockKeyService
    {
        Task PayAsync(PayRequest request);
    }

    public interface IConflict
    {
        Task DoAsync(long id);
    }

    public interface INonVirtual
    {
        Task DoAsync(long id);
    }

    public sealed class PayRequest : ILockKey
    {
        public long OrderId { get; set; }

        public string? Note { get; set; }

        public string ToLockKey() => OrderId.ToString();
    }

    public sealed class SampleService : ISample
    {
        public Task PayAsync(long orderId, PayRequest request) => Task.CompletedTask;
    }

    public class NoKeyService : INoKey
    {
        [VivDistributedLock("test")]
        public virtual Task DoAsync(long id) => Task.CompletedTask;
    }

    public class LockKeyService : ILockKeyService
    {
        [VivDistributedLock("test")]
        public virtual Task PayAsync(PayRequest request) => Task.CompletedTask;
    }

    public class ConflictService : IConflict
    {
        [VivDistributedLock("test", Global = true, KeyTemplate = "{id}")]
        public virtual Task DoAsync(long id) => Task.CompletedTask;
    }

    public class NonVirtualService : INonVirtual
    {
        [VivDistributedLock("test", KeyTemplate = "{id}")]
        public Task DoAsync(long id) => Task.CompletedTask;
    }

    /// <summary>没有业务接口 + public virtual —— 走类代理，可以拦截</summary>
    public class NoInterfaceLockService
    {
        [VivDistributedLock("test", Global = true)]
        public virtual Task DoAsync() => Task.CompletedTask;
    }

    /// <summary>没有业务接口 + 非 virtual —— 类代理拦不到</summary>
    public class NoInterfaceNonVirtualLockService
    {
        [VivDistributedLock("test", Global = true)]
        public Task DoAsync() => Task.CompletedTask;
    }

    /// <summary>没有业务接口 + sealed —— Castle 生成不出子类</summary>
    public sealed class SealedNoInterfaceLockService
    {
        [VivDistributedLock("test", Global = true)]
        public Task DoAsync() => Task.CompletedTask;
    }

    /// <summary>没有业务接口 + internal —— 动态程序集继承不了</summary>
    internal class InternalNoInterfaceLockService
    {
        [VivDistributedLock("test", Global = true)]
        public virtual Task DoAsync() => Task.CompletedTask;
    }

    /// <summary>没有业务接口 + 类级特性 + 非 virtual 方法</summary>
    [VivDistributedLock("test", Global = true)]
    public class ClassLevelNoInterfaceLockService
    {
        public Task DoAsync() => Task.CompletedTask;
    }
}

