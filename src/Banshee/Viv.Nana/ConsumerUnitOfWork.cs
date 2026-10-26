using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;

namespace Viv.Nana
{
    /// <summary>
    /// 消费者侧工作单元 —— <c>VivConsumer</c> / <c>VivLocalConsumer</c> 的 HandleAsync
    /// 不走 Castle 接口代理，由这里按类型上的 <c>[VivUnitOfWork]</c> 显式开合事务。
    ///
    /// 提交发生在 <c>ReceiveMessageAsync</c> 成功返回之后、本地事件 Flush 之前；
    /// 失败 / 重投 / 抛异常则回滚，外层 finally 会 Discard。
    /// </summary>
    internal static class ConsumerUnitOfWork
    {
        private static readonly ConcurrentDictionary<Type, bool> EnabledCache = new();

        /// <summary>
        /// 该类（或其 <c>ReceiveMessageAsync</c>）是否启用了工作单元。
        /// </summary>
        public static bool IsEnabled(Type consumerType)
            => EnabledCache.GetOrAdd(consumerType, Detect);

        /// <summary>
        /// 标了特性却拿不到 <see cref="IVivUnitOfWork"/> 时立刻失败，避免「标了但没事务」静默裸奔。
        /// </summary>
        public static void EnsureAvailable(Type consumerType, IVivUnitOfWork? unitOfWork)
        {
            if (IsEnabled(consumerType) && unitOfWork is null)
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在 {consumerType.FullName} 上，但 IVivUnitOfWork 未注入 —— " +
                    "消费者事务由 HandleAsync 显式开启，请配置 DatabaseOption，或移除该特性。");
            }
        }

        /// <summary>
        /// 需要事务时包住 <paramref name="work"/>：成功提交，失败信封或异常回滚。不需要则原样执行。
        ///
        /// 令牌只用于开事务；提交 / 回滚是收尾动作，不跟停机令牌走 ——
        /// 半提交半丢比慢一下糟得多。与拦截器 <c>VivUnitOfWorkInterceptor</c> 同一取舍。
        /// </summary>
        public static async Task<SubscribeResult> ExecuteAsync(
            IVivUnitOfWork? unitOfWork,
            Type consumerType,
            Func<CancellationToken, Task<SubscribeResult>> work,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(work);

            if (!IsEnabled(consumerType))
                return await work(cancellationToken).ConfigureAwait(false);

            EnsureAvailable(consumerType, unitOfWork);

            return await unitOfWork!.ExecuteAsync(
                work,
                commitWhen: static result => result.IsSuccess,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private static bool Detect(Type consumerType)
        {
            var classAttr = consumerType.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);
            if (classAttr != null)
                return classAttr.Enabled;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var method in consumerType.GetMethods(flags))
            {
                if (method.Name != "ReceiveMessageAsync")
                    continue;

                var methodAttr = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);
                if (methodAttr != null)
                    return methodAttr.Enabled;
            }

            return false;
        }
    }
}
