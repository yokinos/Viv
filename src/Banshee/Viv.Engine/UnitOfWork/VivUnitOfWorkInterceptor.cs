using Castle.DynamicProxy;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// <c>[VivUnitOfWork]</c> 的拦截器 —— 进方法开事务，正常结束提交，异常或失败信封回滚。
    ///
    /// 继承 <see cref="AsyncInterceptorBase"/> 而不是 Castle 的 IInterceptor：后者的 Intercept 是同步的，
    /// invocation.Proceed() 在业务遇到第一个 await 时就返回了，写成 Proceed(); Commit(); 会在业务
    /// 真正跑完之前就提交，事务边界完全错位且不报错。AsyncInterceptorBase 把调用拆成
    /// 同步 / Task / Task&lt;T&gt; 三分法，「提交」才能挂在业务 Task 真正完成之后。
    /// ValueTask / ValueTask&lt;T&gt; 实测不能安全走这条链，注册期直接拒绝。
    ///
    /// 注册时要垫一层 <c>AsyncDeterminationInterceptor</c>：InterceptedBy 只认 IInterceptor，
    /// 而本类实现的是 IAsyncInterceptor，直接挂上去解析时会抛 InvalidCastException。
    ///
    /// InstancePerLifetimeScope —— 否则 <see cref="IVivUnitOfWork"/> 会变成全局单例，
    /// 并发请求共用同一个事务状态机。（已实测 Autofac 的接口代理从当前作用域解析拦截器，不会落根。）
    ///
    /// 调用方的 <see cref="CancellationToken"/> 只传给 <c>BeginAsync</c>（请求已经断了就别开事务）；
    /// Commit 与 Rollback 一律 <see cref="CancellationToken.None"/> —— 它们是收尾动作，不是业务步骤。
    /// MVC action 的令牌是 <c>HttpContext.RequestAborted</c>，客户端断线就取消：提交被取消的后果是
    /// 「业务成功返回、写却全丢」，回滚被取消则留下没关掉的事务。与本地事件分发那几处传 None 同一取舍。
    /// </summary>
    internal sealed class VivUnitOfWorkInterceptor : AsyncInterceptorBase
    {
        private readonly IVivUnitOfWork _unitOfWork;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 方法 → 是否要开事务。反射查特性有成本，这里每次调用都跑，所以缓存。
        /// 方法集合在运行期不变，缓存无需失效。
        /// </summary>
        private static readonly ConcurrentDictionary<MethodInfo, bool> _decisionCache = new();

        public VivUnitOfWorkInterceptor(IVivUnitOfWork unitOfWork, ILoggerContract logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// 无返回值的 Task 方法 —— 判不了信封，只看有没有抛异常。
        /// </summary>
        protected override async Task InterceptAsync(IInvocation invocation, IInvocationProceedInfo proceedInfo, Func<IInvocation, IInvocationProceedInfo, Task> proceed)
        {
            if (!ShouldIntercept(invocation))
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
                return;
            }

            var cancellationToken = ResolveCancellationToken(invocation);
            await using var transaction = await _unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;                                          // 原样上抛，绝不吞
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 有返回值的 Task&lt;T&gt; 方法。T 是拆包之后的返回值，所以这里能直接判信封码。
        /// </summary>
        protected override async Task<TResult> InterceptAsync<TResult>(IInvocation invocation, IInvocationProceedInfo proceedInfo, Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
        {
            if (!ShouldIntercept(invocation))
            {
                return await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }

            var cancellationToken = ResolveCancellationToken(invocation);
            await using var transaction = await _unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);

            TResult result;
            try
            {
                result = await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            // 业务自己返回失败信封（HTTP 仍是 200）→ 回滚。
            // 与本地事件分发的成败判定同源，见 FailDetector。
            if (FailDetector.IsFailed(result))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _logger.Warning("方法返回失败信封，事务已回滚：{0}", Describe(invocation));
                return result;
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 这次调用要不要开事务。
        ///
        /// 特性标在实现类的方法上，所以要读 <c>MethodInvocationTarget</c> ——
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

        /// <summary>
        /// 从被拦方法的实参里取出 <see cref="CancellationToken"/>（通常是最后一个参数），
        /// 只给 <c>BeginAsync</c> 用，见类注释。没有则 <see cref="CancellationToken.None"/>。
        /// </summary>
        private static CancellationToken ResolveCancellationToken(IInvocation invocation)
        {
            var arguments = invocation.Arguments;
            for (var i = arguments.Length - 1; i >= 0; i--)
            {
                if (arguments[i] is CancellationToken token)
                    return token;
            }

            return CancellationToken.None;
        }

        private static string Describe(IInvocation invocation) => $"{invocation.TargetType?.Name}.{invocation.Method.Name}";
    }
}
