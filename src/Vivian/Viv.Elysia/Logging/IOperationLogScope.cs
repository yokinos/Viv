namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 业务侧补充日志内容的入口。过滤器已经把「谁、什么时候、哪个接口、结果如何」全自动采集了，
    /// 这里只补两件它猜不到的事：<b>操作对象</b>和<b>定制正文</b>。
    ///
    /// <para><b>只在 action 执行期间有效</b>：内容挂在当前请求上（<c>HttpContext.Items</c>），
    /// 请求结束即消失。没有 HttpContext 的地方（Worker / 定时任务）请直接用
    /// <see cref="IOperationLogWriter.WriteAsync"/> 主动写 —— 那里的"这次请求"根本不存在，
    /// 硬塞进作用域只会得到一条静默丢失的日志（这正是旧 <c>SetLog</c> 的空洞）。</para>
    ///
    /// <para>没注入也没关系：接口标了 <c>[OperationLog]</c> 照样记，只是少了对象标识。</para>
    /// </summary>
    public interface IOperationLogScope
    {
        /// <summary>
        /// 覆盖日志正文。适合"改了什么"这种要读得懂的场景：
        /// <c>scope.Describe($"将 {user.Name} 的角色从 {oldRole} 调整为 {newRole}")</c>
        /// </summary>
        void Describe(string description);

        /// <summary>
        /// 标注本次操作的对象 —— 审计里最值钱的字段，按它才能反查"谁动过这条数据"。
        /// <para>常见时机是 Service 里已经查出主键之后：<c>scope.Target("User", user.Id)</c>。
        /// 对象 id 需要业务才知道，过滤器不可能从参数里猜。</para>
        /// </summary>
        /// <param name="type">对象类型，如 <c>User</c> / <c>Order</c>。用类型名而不是中文，便于按类型检索。</param>
        /// <param name="id">对象标识，内部 <c>ToString()</c> 后落库。</param>
        void Target(string type, object? id);

        /// <summary>
        /// 明确不记这一条。语义与"没标特性"不同：它用于抵消类级/全局的默认行为，
        /// 比如补偿请求、幂等重放、预检查这类"调了但不算一次业务操作"的调用。
        /// </summary>
        void Skip();
    }
}
