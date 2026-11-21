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
    public void 基类方法带特性_派生类重写不标_进拦截清单()
    {
        // 运行期 ResolveAttribute 用 inherit: true，沿 override 链找得到基类那条特性。
        // 注册期只看 DeclaredOnly + inherit: false 的话，这条整段跳过校验 ——
        // 「标了特性 = 有锁」不成立的方法就漏到运行期了。
        var types = DistributedLockRegistration.Resolve(
            [typeof(OverrideWithoutAttributeService)], [], lockEnabled: true);

        Assert.Contains(typeof(OverrideWithoutAttributeService), types);
    }

    [Fact]
    public void 基类方法带特性_派生类不重写_也要进拦截清单()
    {
        // 最危险的一种：HasAttribute 用 DeclaredOnly 枚举不到继承来的方法 → 类型不进拦截清单
        // → 拦截器根本不挂 → 能编译、能跑、就是没锁。运行期其实查得到（MethodInvocationTarget
        // 就是基类那个方法），纯粹是注册期漏判。
        var types = DistributedLockRegistration.Resolve([typeof(NoOverrideLockService)], [], lockEnabled: true);

        Assert.Contains(typeof(NoOverrideLockService), types);
    }

    [Fact]
    public void 基类特性_派生类重写了形参名_启动失败()
    {
        // 模板来自基类、签名来自派生类 —— 只有把继承来的特性拿到派生类方法上校验才查得出来，
        // 否则运行期每次调用都在解析 Key 时炸。
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(OverrideRenamedParameterService)], [], lockEnabled: true));

        Assert.Contains("{id}", ex.Message);
        Assert.Contains("找不到同名参数", ex.Message);
    }

    [Fact]
    public void 基类特性_派生类不重写且方法非virtual_启动失败()
    {
        // 没有业务接口只能类代理；基类那个方法非 virtual，代理拦不到 —— 锁静默失效，启动期挡住
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(DerivedNoInterfaceLockService)], [], lockEnabled: true));

        Assert.Contains(nameof(InheritedNoInterfaceBase.DoAsync), ex.Message);
        Assert.Contains("virtual", ex.Message);
        Assert.Contains("声明在", ex.Message);
    }

    [Fact]
    public void 有接口_但方法不在接口上_启动失败()
    {
        // 有业务接口就只按接口注册（AsImplementedInterfaces，不注册 AsSelf），
        // 这个方法从容器里根本调不到 —— 注册期不报错的话，它永远安安静静地没有锁。
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(NotOnInterfaceService)], [], lockEnabled: true));

        Assert.Contains(nameof(NotOnInterfaceService.HelperAsync), ex.Message);
        Assert.Contains("不是任何业务接口的成员", ex.Message);
    }

    [Fact]
    public void 类级特性_本类方法不在接口上_启动失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DistributedLockRegistration.Resolve([typeof(ClassLevelNotOnInterfaceService)], [], lockEnabled: true));

        Assert.Contains(nameof(ClassLevelNotOnInterfaceService.HelperAsync), ex.Message);
        Assert.Contains("不是任何业务接口的成员", ex.Message);
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

    /// <summary>基类标了方法级特性 —— 派生类重写（自己不标）/ 不重写两种形态都要被看见</summary>
    public class InheritedLockBase : IInheritedLock
    {
        [VivDistributedLock("test", KeyTemplate = "{id}")]
        public virtual Task DoAsync(long id) => Task.CompletedTask;
    }

    public interface IInheritedLock
    {
        Task DoAsync(long id);
    }

    /// <summary>重写了但自己不标特性 —— 运行期能沿 override 链查到，注册期不能跳过校验</summary>
    public class OverrideWithoutAttributeService : InheritedLockBase
    {
        public override Task DoAsync(long id) => Task.CompletedTask;
    }

    /// <summary>不重写 —— 继承来的方法同样会被代理命中，类型必须进拦截清单</summary>
    public class NoOverrideLockService : InheritedLockBase;

    /// <summary>重写时改了形参名：基类模板 {id} 对不上，必须启动期报错</summary>
    public class OverrideRenamedParameterService : InheritedLockBase
    {
        public override Task DoAsync(long renamed) => Task.CompletedTask;
    }

    /// <summary>没有接口 + 基类方法非 virtual + 派生类不重写 —— 类代理拦不到</summary>
    public class InheritedNoInterfaceBase
    {
        [VivDistributedLock("test", Global = true)]
        public Task DoAsync() => Task.CompletedTask;
    }

    public class DerivedNoInterfaceLockService : InheritedNoInterfaceBase;

    public interface IOnlyDoAsync
    {
        Task DoAsync(long id);
    }

    /// <summary>有业务接口，但 HelperAsync 不在接口上 —— 容器里调不到，锁不可能生效</summary>
    public class NotOnInterfaceService : IOnlyDoAsync
    {
        public virtual Task DoAsync(long id) => Task.CompletedTask;

        [VivDistributedLock("test", Global = true)]
        public virtual Task HelperAsync() => Task.CompletedTask;
    }

    /// <summary>类级特性 + 本类 public 方法不在接口上 —— 类级锁同样覆盖不到</summary>
    [VivDistributedLock("test", Global = true)]
    public class ClassLevelNotOnInterfaceService : IOnlyDoAsync
    {
        public virtual Task DoAsync(long id) => Task.CompletedTask;

        public virtual Task HelperAsync() => Task.CompletedTask;
    }
}

