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
        // 接口代理按接口调用、反射转给目标实例，非 virtual 实现同样能拦到
        var types = DistributedLockRegistration.Resolve([typeof(NonVirtualService)], [], lockEnabled: true);

        Assert.Contains(typeof(NonVirtualService), types);
    }

    [Fact]
    public void 默认失败文案()
    {
        var attribute = new VivDistributedLockAttribute("test");

        Assert.Equal("请勿重复提交，请稍等", attribute.Message);
        Assert.Equal(VivDistributedLockAttribute.DefaultBusyMessage, attribute.Message);
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
}

