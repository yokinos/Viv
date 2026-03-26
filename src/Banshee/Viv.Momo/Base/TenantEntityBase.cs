using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Enums;

namespace Viv.Momo.Base
{
    /// <summary>
    /// Viv框架所有业务实体的基类
    /// </summary>
    public class TenantEntityBase : EntityBase
    {
        /// <summary>
        /// 租户ID（多租户隔离）
        /// </summary>
        public long TenantId { get; set; }
    }
}
