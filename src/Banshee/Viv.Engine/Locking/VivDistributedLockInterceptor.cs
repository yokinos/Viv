using Castle.DynamicProxy;
using System.Collections.Concurrent;
using System.Reflection;
using Viv.Contracts;
using Viv.Contracts.Attributes;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.Locking
{
    /// <summary>
    /// <c>[VivDistributedLock]</c> 的拦截器：进方法取锁，正常/异常结束都释放。
    ///
    /// <para><b>执行流程</b></para>
    /// <code>
    /// 解析特性 → 拼 key → AcquireLockWithRetryAsync
    ///     ├─ 成功            → Proceed → finally ReleaseLockAsync
    ///     ├─ 失败且 FailOpen → 记 Warning → Proceed（无锁）
    ///     └─ 失败            → 抛 DistributedLockException
    /// </code>
    ///
    /// <para><b>Key 格式</b></para>
    /// <list type="bullet">
    /// <item><description><c>ILockKey</c> 参数：<c>lock:aop:{Prefix}:{租户}:{ToLockKey()}</c></description></item>
    /// <item><description><c>KeyTemplate</c>：<c>lock:aop:{Prefix}:{模板解析结果}</c>（模板自己决定要不要带租户）</description></item>
    /// <item><description><c>Global</c>：<c>lock:aop:{Prefix}:{实现类名}.{方法名}</c></description></item>
    /// </list>
    ///
    /// <para><b>顺序</b>：与 <c>VivUnitOfWorkInterceptor</c> 同时命中时，注册顺序把本拦截器放在外层 ——
    /// 先拿锁再开事务，提交之后才释放锁，避免等锁期间占着数据库连接。</para>
    ///
    /// <para><b>异常</b>：取锁失败默认 fail-closed 抛 <see cref="DistributedLockException"/>；
    /// Key 解析为空抛 <see cref="DistributedLockKeyException"/>；释放失败只记日志，不覆盖业务异常。</para>
    /// </summary>
    internal sealed class VivDistributedLockInterceptor : AsyncInterceptorBase
    {
        private readonly IDistributedLock _distributedLock;
        private readonly IVivContext _context;
        private readonly ILoggerContract _logger;

        /// <summary>方法 → 特性。反射查特性有成本，每次调用都跑，缓存在这里；方法集合运行期不变。</summary>
        private static readonly ConcurrentDictionary<MethodInfo, VivDistributedLockAttribute?> _attributeCache = new();

        public VivDistributedLockInterceptor(
            IDistributedLock distributedLock,
            IVivContext context,
            ILoggerContract logger)
        {
            _distributedLock = distributedLock;
            _context = context;
            _logger = logger;
        }

        protected override async Task InterceptAsync(IInvocation invocation, IInvocationProceedInfo proceedInfo,
            Func<IInvocation, IInvocationProceedInfo, Task> proceed)
        {
            var attribute = ResolveAttribute(invocation);
            if (attribute is null)
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
                return;
            }

            var key = BuildKey(attribute, invocation);
            if (!await TryAcquireAsync(attribute, invocation, key).ConfigureAwait(false))
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
                return;
            }

            try
            {
                await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(attribute, key).ConfigureAwait(false);
            }
        }

        protected override async Task<TResult> InterceptAsync<TResult>(IInvocation invocation,
            IInvocationProceedInfo proceedInfo, Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
        {
            var attribute = ResolveAttribute(invocation);
            if (attribute is null)
            {
                return await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }

            var key = BuildKey(attribute, invocation);
            if (!await TryAcquireAsync(attribute, invocation, key).ConfigureAwait(false))
            {
                return await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }

            try
            {
                return await proceed(invocation, proceedInfo).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(attribute, key).ConfigureAwait(false);
            }
        }

        /// <summary>返回 true 表示已持锁，false 表示按 FailOpen 放弃加锁继续执行。</summary>
        private async Task<bool> TryAcquireAsync(VivDistributedLockAttribute attribute, IInvocation invocation, string key)
        {
            var expire = TimeSpan.FromSeconds(attribute.ExpireSeconds > 0 ? attribute.ExpireSeconds : 30);
            var retryCount = attribute.RetryCount > 0 ? attribute.RetryCount : 0;
            var retryDelay = attribute.RetryDelayMilliseconds > 0 ? attribute.RetryDelayMilliseconds : 200;

            bool acquired;
            try
            {
                acquired = await _distributedLock.AcquireLockWithRetryAsync(
                    key,
                    expire,
                    isReentrant: attribute.Reentrant,
                    maxRetryCount: retryCount,
                    baseDelay: retryDelay,
                    maxDelay: retryDelay).ConfigureAwait(false);
            }
            catch (DistributedLockException ex) when (attribute.FailOpen)
            {
                _logger.Warning("分布式锁不可用，按 FailOpen 继续执行：{0}，{1}", Describe(invocation), ex.Message);
                return false;
            }

            if (acquired) return true;

            if (attribute.FailOpen)
            {
                _logger.Warning("未取得分布式锁，按 FailOpen 继续执行：{0}，Key：{1}", Describe(invocation), key);
                return false;
            }

            throw new DistributedLockException(attribute.Message, key, retryCount);
        }

        private async Task ReleaseAsync(VivDistributedLockAttribute attribute, string key)
        {
            try
            {
                await _distributedLock.ReleaseLockAsync(key, isReentrant: attribute.Reentrant).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 释放失败不能盖掉业务异常；锁靠过期兜底
                _logger.Error("释放分布式锁失败：{0}", ex, key);
            }
        }

        private string BuildKey(VivDistributedLockAttribute attribute, IInvocation invocation)
        {
            var method = invocation.MethodInvocationTarget ?? invocation.Method;
            var prefix = LockKeyMagic.BusinessPrefix + "aop:";

            if (attribute.Global)
            {
                return LockKeyMagic.Join(prefix, attribute.Prefix,
                    method.DeclaringType?.Name ?? "?", method.Name);
            }

            if (!string.IsNullOrWhiteSpace(attribute.KeyTemplate))
            {
                // 模板即完整业务 key，不再自动补租户
                var body = LockKeyTemplate.Resolve(
                    attribute.KeyTemplate,
                    method.GetParameters(),
                    invocation.Arguments,
                    _context.SubjectId,
                    _context.UserId,
                    _context.AppId);

                return LockKeyMagic.Join(prefix, attribute.Prefix, body);
            }

            var lockKey = invocation.Arguments.OfType<ILockKey>().FirstOrDefault()
                          ?? throw new DistributedLockKeyException($"无法从参数中解析锁 Key：{Describe(invocation)}");

            var value = lockKey.ToLockKey();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DistributedLockKeyException($"ILockKey.ToLockKey() 返回空值：{Describe(invocation)}");
            }

            // DTO 只给业务段，租户由框架从上下文补
            return LockKeyMagic.Join(prefix, attribute.Prefix, _context.SubjectId, value);
        }

        private static VivDistributedLockAttribute? ResolveAttribute(IInvocation invocation)
        {
            // 走接口代理时 invocation.Method 是接口方法，特性在实现类上，要读 MethodInvocationTarget
            var method = invocation.MethodInvocationTarget ?? invocation.Method;
            return _attributeCache.GetOrAdd(method, static m =>
            {
                var attribute = m.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true)
                                ?? m.DeclaringType?.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true);

                return attribute is { Enabled: true } ? attribute : null;
            });
        }

        private static string Describe(IInvocation invocation)
            => $"{invocation.TargetType?.Name}.{invocation.Method.Name}";
    }
}
