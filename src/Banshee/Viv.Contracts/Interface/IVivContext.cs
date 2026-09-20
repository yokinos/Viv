using Viv.Contracts.Models;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// Viv 请求上下文 — 贯穿整个请求生命周期的核心标识。
    /// <list type="bullet">
    /// <item><description>数据来源：VivContextMiddleware 经 <see cref="IVivContextProvider"/> 组装 <see cref="VivContextContent"/>，通过 <see cref="IVivContextAccessor"/> 存入当前请求异步上下文。默认实现先读身份头（网关签发），没有才回落 JWT Token。</description></item>
    /// <item><description>数据库操作：自动基于主体 ID 完成数据隔离。</description></item>
    /// <item><description>消息发布：事件信封自动携带身份信息。</description></item>
    /// <item><description>业务判断：提供 AppId / SubjectId / UserId 用于业务分支判定。</description></item>
    /// </list>
    /// </summary>
    public interface IVivContext
    {
        /// <summary>
        /// 客户端应用Id
        /// </summary>
        long AppId { get; }

        /// <summary>
        /// 主体Id（TenantId / CompanyId / OrgId）
        /// </summary>
        long SubjectId { get; }

        /// <summary>
        /// 当前登录用户Id
        /// </summary>
        long UserId { get; }

        /// <summary>
        /// 请求Id（唯一标识当前请求）
        /// </summary>
        string TraceId { get; }

        /// <summary>
        /// 设置上下文快照
        /// </summary>
        void SetSnapshot(VivContextContent model);

        /// <summary>
        /// 清空上下文
        /// 只由框架触发点调用（VivContextMiddleware 的 finally、VivConsumer / VivLocalConsumer 消息消费结束时），业务代码不要调
        /// </summary>
        void Clear();

        /// <summary>
        /// 获取原始快照（谨慎使用，优先使用封装属性）
        /// </summary>
        VivContextContent? GetRawSnapshot();
    }
}