using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 集团公司主体，多个租户机构可归属同一集团
    /// </summary>
    public class AtCompany : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 所属售卖平台ID（关联AtOrg.Id）
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 集团登录编码，唯一
        /// </summary>
        [StringLength(64)]
        public string? CompanyCode { get; set; }

        /// <summary>
        /// 集团名称
        /// </summary>
        [StringLength(120)]
        public string? Name { get; set; }

        /// <summary>
        /// 统一社会信用代码
        /// </summary>
        [StringLength(50)]
        public string? CreditCode { get; set; }

        /// <summary>
        /// 对接联系人
        /// </summary>
        [StringLength(30)]
        public string? ContactName { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [StringLength(20)]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 地址
        /// </summary>
        [StringLength(300)]
        public string? Address { get; set; }

        /// <summary>
        /// 最大机构数量
        /// </summary>
        public int? MaxTenantCount { get; set; }

        /// <summary>
        /// 备注说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        public EmStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public long? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}