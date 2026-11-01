using Castle.DynamicProxy;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// <c>InterceptedBy</c> 只认 <see cref="IInterceptor"/>，而 <see cref="VivUnitOfWorkInterceptor"/>
    /// 继承的 <c>AsyncInterceptorBase</c> 实现的是 <c>IAsyncInterceptor</c> —— 中间垫一层适配器。
    ///
    /// 单独派生一个类型（而不是直接用 <c>AsyncDeterminationInterceptor</c>）是为了让
    /// 「同时带 [VivUnitOfWork] 和 [VivDistributedLock] 的类型」能挂两个不同 Type 的拦截器：
    /// <c>InterceptedBy</c> 按 Type 解析，同一个 Type 注册两次拿不到两个实例。
    /// </summary>
    internal sealed class UnitOfWorkInterceptorAdapter : AsyncDeterminationInterceptor
    {
        public UnitOfWorkInterceptorAdapter(VivUnitOfWorkInterceptor interceptor)
            : base(interceptor)
        {
        }
    }
}
