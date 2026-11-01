using Castle.DynamicProxy;

namespace Viv.Engine.Locking
{
    /// <summary>
    /// <c>InterceptedBy</c> 只认 <see cref="IInterceptor"/>，而 <see cref="VivDistributedLockInterceptor"/>
    /// 继承的 <c>AsyncInterceptorBase</c> 实现的是 <c>IAsyncInterceptor</c> —— 中间垫一层适配器，
    /// 与工作单元同一个做法。不能用同一个 <c>AsyncDeterminationInterceptor</c> 类型注册两次，
    /// 所以这里单独派生一个类型，供 <c>InterceptedBy(typeof(DistributedLockInterceptorAdapter))</c> 解析。
    /// </summary>
    internal sealed class DistributedLockInterceptorAdapter : AsyncDeterminationInterceptor
    {
        public DistributedLockInterceptorAdapter(VivDistributedLockInterceptor interceptor)
            : base(interceptor)
        {
        }
    }
}
