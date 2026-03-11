using System;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// Viv框架上下文核心接口（承载用户登录后的基础标识）
    /// </summary>
    public interface IVivContext
    {
        /// <summary>
        /// Viv应用唯一标识
        /// </summary>
        long AppId { get; }

        /// <summary>
        /// SaaS租户唯一标识（多租户隔离核心）
        /// </summary>
        long TenantId { get; }

        /// <summary>
        /// 当前登录用户ID
        /// </summary>
        long UserId { get; }

        /// <summary>
        /// 清除上下文信息（框架内部调用，禁止业务代码手动调用）
        /// </summary>
        void Clear();

        /// <summary>
        /// 设置租户ID
        /// </summary>
        void SetTenantId(long tenantId);

        /// <summary>
        /// 设置应用ID
        /// </summary>
        void SetAppId(long appId);

        /// <summary>
        /// 设置登录用户ID
        /// </summary>
        void SetUserId(long userId);
    }
}