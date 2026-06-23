namespace Viv.Contracts.Interface
{
    /// <summary>
    /// Viv 请求上下文 — 贯穿整个请求生命周期的核心标识。
    ///
    /// 数据来源：
    /// <see cref="VivContextMiddleware"/> 从 HTTP Header 中读取 Viv_AppId / Viv_TenantId / Viv_UserId，
    /// 注入到 <see cref="IVivContext"/>（Scoped，底层由 <see cref="System.Threading.AsyncLocal{T}"/> 保证异步安全）。
    ///
    /// 使用场景：
    /// - 数据库操作：自动按 TenantId 行级隔离
    /// - 消息发布：NanaEventPublisher 将 AppId/TenantId 写入 NanaEnvelope 信封
    /// - 日志追踪：跨服务传递请求来源
    /// - 业务判断：按 AppId / TenantId / UserId 路由逻辑
    /// </summary>
    public interface IVivContext
    {
        /// <summary>
        /// 客户端应用 ID — 标识请求来源（App / 定时任务站点 / 第三方系统）
        /// </summary>
        long AppId { get; }

        /// <summary>
        /// 租户 ID — 多租户数据隔离核心标识
        /// </summary>
        long TenantId { get; }

        /// <summary>
        /// 当前登录用户 ID
        /// </summary>
        long UserId { get; }

        /// <summary>
        /// 设置客户端应用 ID
        /// </summary>
        void SetAppId(long appId);

        /// <summary>
        /// 设置租户 ID
        /// </summary>
        void SetTenantId(long tenantId);

        /// <summary>
        /// 设置登录用户 ID
        /// </summary>
        void SetUserId(long userId);

        /// <summary>
        /// 清除上下文 — 请求结束时由中间件调用，禁止业务代码手动调用
        /// </summary>
        void Clear();
    }
}
