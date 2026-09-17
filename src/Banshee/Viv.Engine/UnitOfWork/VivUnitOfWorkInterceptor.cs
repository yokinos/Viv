using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// <c>[VivUnitOfWork]</c> 的拦截器 —— 进方法开事务，正常结束提交，异常或失败信封回滚。
    ///
    /// 【为什么继承 <see cref="AsyncInterceptorBase"/> 而不是直接实现 Castle 的 IInterceptor】
    /// Castle 的 <c>IInterceptor.Intercept</c> 是<b>同步</b>的：<c>invocation.Proceed()</c> 在业务方法
    /// 遇到第一个 <c>await</c> 时就返回了。天真地写 <c>Proceed(); Commit();</c> 会在业务真正跑完之前
    /// 就提交 —— 事务边界完全错位，而且不报错。<c>AsyncInterceptorBase</c> 把调用拆成
    /// 「同步 / Task / Task&lt;T&gt;」三分法，让「提交」能挂在业务 Task 真正完成之后。
    /// （有测试钉着这一点：业务方法内含 await 时，提交必须发生在方法体结束之后。）
    ///
    /// 【必须垫一层 AsyncDeterminationInterceptor】
    /// <c>InterceptedBy</c> 只认 <c>IInterceptor</c>，而本类实现的是 <c>IAsyncInterceptor</c> ——
    /// 直接挂上去会在解析时抛 <c>InvalidCastException</c>。注册时用
    /// <c>AsyncDeterminationInterceptor</c> 包一层（见 <c>VivAutofacRegister</c>）。
    ///
    /// 【作用域】本类是 <c>InstancePerLifetimeScope</c>。已实测 Autofac 的接口代理从<b>当前</b>作用域
    /// 解析拦截器，不会落到根作用域 —— 否则 <see cref="IVivUnitOfWork"/> 会变成全局单例，
    /// 并发请求共用同一个事务状态机。
    /// </summary>
    internal sealed class VivUnitOfWorkInterceptor : AsyncInterceptorBase
    {
        private readonly IVivUnitOfWork _unitOfWork;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 方法 → 是否要开事务。反射查特性有成本，而这里在<b>每次调用</b>上都跑，
        /// 所以缓存起来。方法集合在运行期不变，缓存无需失效。
        /// </summary>
        private static readonly ConcurrentDictionary<MethodInfo, bool> _decisionCache = new();

        public VivUnitOfWorkInterceptor(IVivUnitOfWork unitOfWork, ILoggerContract logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>无返回值的 Task 方法 —— 判不了信封，只看有没有抛异常。</summary>
        protected override async Task InterceptAsync(
            IInvocation invocation,
            IInvocationProceedInfo proceedInfo,
            Func<IInvocation, IInvocationProceedInfo, Task> proceed)
        {
            if (!ShouldIntercept(invocation))
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
                return;
            }

            await using var transaction = await _unitOfWork.BeginAsync().ConfigureAwait(false);
            try
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                throw;                                          // 原样上抛，绝不吞
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 有返回值的 Task&lt;T&gt; 方法。T 是<b>拆包之后</b>的返回值，
        /// 所以这里能直接判 <c>VivApiResult</c> 的信封码。
        /// </summary>
        protected override async Task<TResult> InterceptAsync<TResult>(
            IInvocation invocation,
            IInvocationProceedInfo proceedInfo,
            Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
        {
            if (!ShouldIntercept(invocation))
            {
                return await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }

            await using var transaction = await _unitOfWork.BeginAsync().ConfigureAwait(false);

            TResult result;
            try
            {
                result = await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            // 业务自己返回失败信封（HTTP 仍是 200）→ 回滚。
            // 与本地事件分发的成败判定同源，见 FailDetector。
            if (FailDetector.IsFailed(result))
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                _logger.Warning("方法返回失败信封，事务已回滚：{0}", Describe(invocation));
                return result;
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 这次调用要不要开事务。
        ///
        /// 特性标在<b>实现类</b>的方法上，所以要读 <c>MethodInvocationTarget</c> ——
        /// 走接口代理时 <c>invocation.Method</c> 是接口上的方法，在它身上查特性什么都查不到。
        /// </summary>
        private static bool ShouldIntercept(IInvocation invocation)
        {
            var method = invocation.MethodInvocationTarget ?? invocation.Method;
            return _decisionCache.GetOrAdd(method, static m =>
            {
                var attribute = m.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true)
                                ?? m.DeclaringType?.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);

                return attribute is { Enabled: true };
            });
        }

        private static string Describe(IInvocation invocation)
            => $"{invocation.TargetType?.Name}.{invocation.Method.Name}";
    }
}
