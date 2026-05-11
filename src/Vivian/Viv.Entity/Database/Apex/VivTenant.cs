using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 多租户 SaaS系统的核心实体
    /// 如果当前不是SaaS系统,则无需使用此实体
    /// </summary>
    [Table("VivTenant")]
    public class VivTenant : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 租户名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 机构编码（一般用于登录, 或者查询 数据库内保持唯一 会建立索引）
        /// </summary>
        public string? TenantCode { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}
