namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 由参数自己提供分布式锁的业务 key，配合 <c>[VivDistributedLock]</c> 使用。
    ///
    /// <para><b>用法</b></para>
    /// <code>
    /// public sealed class PayRequest : VivApiRequest, ILockKey
    /// {
    ///     public long OrderId { get; set; }
    ///
    ///     // 只返回业务段；租户由框架从 IVivContext.SubjectId 统一拼
    ///     public string ToLockKey() =&gt; OrderId.ToString(CultureInfo.InvariantCulture);
    /// }
    ///
    /// public class OrderService : IOrderService
    /// {
    ///     [VivDistributedLock("order")]
    ///     public Task PayAsync(PayRequest request) =&gt; ...;
    /// }
    ///
    /// // 实际 key：lock:aop:order:1001:42
    /// // 1001 = 当前租户（SubjectId），42 = PayRequest.OrderId
    /// </code>
    ///
    /// <para><b>要求</b></para>
    /// <list type="bullet">
    /// <item><description>只返回业务段（订单号 / 用户 Id 等），不要带租户 —— 客户端传的租户不可信，
    /// 框架统一用 <see cref="IVivContext.SubjectId"/> 拼。</description></item>
    /// <item><description>实现方不该依赖 DI 或 <see cref="IVivContext"/>，保持纯函数、可单测。</description></item>
    /// <item><description>返回空串会在运行期抛 <c>DistributedLockKeyException</c> —— 空 key 会退化成全局锁，必须挡住。</description></item>
    /// <item><description>同一个方法只能有 <b>一个</b> 参数实现本接口；多个会导致启动失败。
    /// 需要组合多个参数时，改用 <c>KeyTemplate</c>。</description></item>
    /// </list>
    /// </summary>
    public interface ILockKey
    {
        /// <summary>
        /// 业务 key，例如订单号 / 用户 Id。只给业务段，租户由框架补。
        /// </summary>
        string ToLockKey();
    }
}

