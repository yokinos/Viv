using System;

namespace Viv.Contracts.Attributes
{
    /// <summary>
    /// 分布式锁 —— 方法执行期间持有 Redis 锁，正常返回或抛异常后释放。
    ///
    /// <para><b>Key 必须能被明确表达，三选一（互斥）：</b></para>
    /// <list type="number">
    /// <item><description>
    /// <see cref="KeyTemplate"/>：占位符取方法参数、参数属性路径或上下文 token
    /// （<c>{tenantId}</c> / <c>{userId}</c> / <c>{appId}</c>）。
    /// 模板即完整业务 key，框架不再自动补租户 —— 要按租户隔离就显式写 <c>{tenantId}</c>。
    /// </description></item>
    /// <item><description>
    /// 参数实现 <see cref="Interface.ILockKey"/>：由参数自己给出业务段，框架自动补租户，
    /// 最终 key 是 <c>lock:aop:{Prefix}:{租户}:{ToLockKey()}</c>。适合请求 DTO 直接带订单号/用户 Id 的写接口。
    /// </description></item>
    /// <item><description>
    /// <see cref="Global"/> = true：整个方法一把锁，key 是
    /// <c>lock:aop:{Prefix}:{实现类名}.{方法名}</c>。适合对账、全量刷新这类确定要全局串行的场景。
    /// </description></item>
    /// </list>
    ///
    /// <para><b>用法 1：请求实现 ILockKey（最常见）</b></para>
    /// <code>
    /// public sealed class PayRequest : VivApiRequest, ILockKey
    /// {
    ///     public long OrderId { get; set; }
    ///
    ///     // 只给业务段，租户由框架统一拼
    ///     public string ToLockKey() =&gt; OrderId.ToString(CultureInfo.InvariantCulture);
    /// }
    ///
    /// public class OrderService : IOrderService
    /// {
    ///     [VivDistributedLock("order")]
    ///     public Task PayAsync(PayRequest request) =&gt; _orders.PayAsync(request);
    /// }
    ///
    /// // 实际 key：lock:aop:order:1001:42（1001 = 租户，42 = OrderId）
    /// </code>
    ///
    /// <para><b>用法 2：Key 模板</b></para>
    /// <code>
    /// [VivDistributedLock("order", KeyTemplate = "{tenantId}:{orderId}")]
    /// public Task RefundAsync(long orderId) =&gt; ...;
    ///
    /// // 占位符支持参数属性路径
    /// [VivDistributedLock("order", KeyTemplate = "{request.OrderId}")]
    /// public Task CancelAsync(CancelRequest request) =&gt; ...;
    /// </code>
    ///
    /// <para><b>用法 3：整个方法一把锁</b></para>
    /// <code>
    /// [VivDistributedLock("reconcile", Global = true)]
    /// public Task ReconcileAsync() =&gt; ...;
    ///
    /// // 实际 key：lock:aop:reconcile:ReconcileService.ReconcileAsync
    /// </code>
    ///
    /// <para><b>约束</b></para>
    /// <list type="bullet">
    /// <item><description>标在实现类的 public 方法上，或标在类上作为该类所有 public 方法的默认值；
    /// 标在接口上会在启动期直接报错（代理只认实现类上的特性）。</description></item>
    /// <item><description>有业务接口的类型走 Castle 接口代理，不要求 virtual；
    /// 没有业务接口的类型走类代理，被拦方法必须是 <c>public virtual</c>，类型必须 public、非 sealed、
    /// 有 public 构造函数 —— 否则注册期直接启动失败。</description></item>
    /// <item><description>方法必须返回 Task / Task&lt;T&gt;；同步方法、ValueTask、泛型方法、静态方法都拦不到，
    /// 注册期直接启动失败。</description></item>
    /// <item><description>类型必须由 <c>DIOption</c> 扫描注册（<c>[VivDependency(AsSelf = true)]</c>
    /// 那条路不挂代理）。接口代理下自调用 <c>this.OtherAsync()</c> 不过代理，特性要标在最外层；
    /// 类代理下自调用会被拦到，同一把 Key 会二次取锁 —— 默认不可重入，必须配
    /// <see cref="Reentrant"/>，或只标最外层。</description></item>
    /// <item><description>消费者（VivConsumer / VivLocalConsumer）不走接口代理，标上去会启动失败；
    /// 消费场景请用 <c>GetConsumeLockKey</c>，或把锁放在消费者调用的 Service 方法上。</description></item>
    /// <item><description>需要配置 Redis（<c>VivOptions.CacheOption.CacheProviderType = Redis</c>），
    /// 否则拿不到 <c>IDistributedLock</c>，启动期报错。</description></item>
    /// </list>
    ///
    /// <para><b>失败语义</b></para>
    /// <list type="bullet">
    /// <item><description>默认失败闭合（fail-closed）：拿不到锁抛 <c>DistributedLockException</c>，
    /// 方法体不会执行；客户端拿到 <c>-203</c>（BusyError）和 <see cref="Message"/> 的文案
    /// （默认「请勿重复提交，请稍等」）。LockKey / RetryCount 只在异常属性与日志里，不返回客户端。</description></item>
    /// <item><description><see cref="FailOpen"/> = true 时，拿不到锁（含 Redis 故障）只记 Warning 并继续执行，
    /// 仅建议读路径或幂等场景使用。</description></item>
    /// <item><description>Key 解析为空（模板占位符为空、<c>ILockKey.ToLockKey()</c> 返回空串）抛
    /// <c>DistributedLockKeyException</c> —— 空 key 会退化成全局锁，必须挡住。</description></item>
    /// </list>
    ///
    /// <para><b>与工作单元的顺序</b>：同时标了 <c>[VivUnitOfWork]</c> 时，锁的拦截器在事务外层 ——
    /// 先拿锁、再开事务，事务提交之后才释放锁，避免等锁期间占着数据库连接。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class VivDistributedLockAttribute : Attribute
    {
        public VivDistributedLockAttribute(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("分布式锁前缀不能为空", nameof(prefix));

            Prefix = prefix.Trim();
        }

        /// <summary>
        /// 锁前缀，参与拼 key：<c>lock:aop:{Prefix}:...</c>。建议用业务名（order / stock / user），
        /// 排查时 <c>keys lock:aop:{Prefix}:*</c> 能直接找出这一族锁。
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// Key 模板，占位符形如 <c>{orderId}</c>、<c>{request.OrderId}</c>、<c>{tenantId}</c>。
        /// 模板即完整业务 key，框架不再自动补租户 —— 需要按租户隔离就显式写 <c>{tenantId}</c>。
        /// 占位符对不上参数名/属性路径会在启动期报错；运行期解析为空抛 <c>DistributedLockKeyException</c>。
        /// 与 <see cref="Global"/>、<see cref="Interface.ILockKey"/> 参数互斥。
        /// </summary>
        public string? KeyTemplate { get; set; }

        /// <summary>
        /// 整个方法一把锁（<c>lock:aop:{Prefix}:{实现类名}.{方法名}</c>）。
        /// 与 <see cref="KeyTemplate"/>、<see cref="Interface.ILockKey"/> 参数互斥。
        /// 适合对账 / 全量刷新这类确定要全局串行的场景，不要用它给按业务 Id 隔离的方法兜底。
        /// </summary>
        public bool Global { get; set; }

        /// <summary>
        /// 锁过期时间（秒），默认 30。锁在持有期间会自动续期，这里管的是进程崩溃后多久自动释放。
        /// </summary>
        public int ExpireSeconds { get; set; } = 30;

        /// <summary>
        /// 拿不到锁时的重试次数，默认 0（只尝试一次）。重试会同步阻塞当前请求，别配太大。
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>重试间隔（毫秒），默认 200；<see cref="RetryCount"/> 为 0 时无意义。</summary>
        public int RetryDelayMilliseconds { get; set; } = 200;

        /// <summary>
        /// 是否允许同一持有者重入，默认 false（不可重入）。
        /// 打开后同一 holder 重复获取同一把锁会累加计数，需成对释放；跨方法嵌套同一 key 时才需要。
        /// </summary>
        public bool Reentrant { get; set; }

        /// <summary>
        /// 取锁失败时返回给客户端的提示文案。
        /// 只在 fail-closed（<see cref="FailOpen"/> = false）时生效；进程内直接调用时它就是异常 Message。
        /// 这个串会<b>原样返回给客户端</b>，别把内部 key / 表名写进来 —— 排查要用的 LockKey 在异常属性里。
        /// </summary>
        public string Message { get; set; } = "请勿重复提交，请稍等";

        /// <summary>
        /// 取锁失败（含 Redis 故障）时是否放行方法体，默认 false（失败闭合，抛 <c>DistributedLockException</c>）。
        /// 只建议读路径或幂等场景打开。
        /// </summary>
        public bool FailOpen { get; set; }

        /// <summary>
        /// 是否启用。标在方法上是豁免那一个方法，标在类上是豁免整个类。
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}

