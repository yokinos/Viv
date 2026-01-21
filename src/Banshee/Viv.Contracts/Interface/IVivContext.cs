using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// Viv 框架的上下文接口（支持SaaS平台）
    /// </summary>
    public interface IVivContext
    {
        /// <summary>
        /// 租户Id (SaaS系统租户标识)
        /// </summary>
        long TenantId { get; set; }

        /// <summary>
        /// 清除上下文信息(不要手动调用)
        /// </summary>
        void Clear();
    }
}
