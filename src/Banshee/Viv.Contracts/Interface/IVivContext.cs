using Viv.Contracts.Models;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// Viv 请求上下文 — 贯穿整个请求生命周期的核心标识。
    ///
    /// 数据来源：
    /// VivContextMiddleware 解析Token之后组装 <see cref="VivContextContent"/>，
    /// 通过 <see cref="IVivContextAccessor"/> 存入当前请求异步上下文。
    ///
    /// 使用场景：
    /// - 数据库操作：自动按主体ID实现数据隔离
    /// - 消息发布：事件信封携带身份信息
    /// - 业务判断：区分 AppId / SubjectId / UserId
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
        string RequestId { get; }

        /// <summary>
        /// 设置上下文快照
        /// </summary>
        void SetSnapshot(VivContextContent model);

        /// <summary>
        /// 清空上下文
        /// 请求结束中间件调用，业务代码禁止调用
        /// </summary>
        void Clear();

        /// <summary>
        /// 获取原始快照（谨慎使用，优先使用封装属性）
        /// </summary>
        VivContextContent? GetRawSnapshot();
    }
}