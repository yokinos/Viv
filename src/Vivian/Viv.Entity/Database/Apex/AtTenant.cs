using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 租户表
    /// </summary>
    public class AtTenant : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 所属组织Id，关联AtOrg.Id
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 所属公司Id，关联AtCompany.Id
        /// </summary>
        public long? CompanyId { get; set; }

        /// <summary>
        /// 租户名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 租户唯一编码
        /// </summary>
        [StringLength(64)]
        public string? Code { get; set; }

        /// <summary>
        /// 租户购买的系统售卖版本Id（关联 <see cref="AtSystemVersion.Id"/>）。
        /// </summary>
        public long? SystemVersionId { get; set; }

        /// <summary>
        /// 到期时间，null代表永久有效
        /// </summary>
        public DateTime? ExpireTime { get; set; }

        /// <summary>
        /// 当前是否可用：<c>Status == 启用</c> 且未到期。
        ///
        /// <para><b>到期做成派生判断，不落成状态</b>：如果把「已到期」写进 <see cref="Status"/>，
        /// 就得有人（定时任务）按时去改它 —— 任务一挂，到期客户照样能用，而且「停用」到底是因为
        /// 到期还是人工封停就分不清了。时间函数交给时间算，状态只表达「人工开关」。</para>
        /// </summary>
        public bool IsActive(DateTimeOffset now) => Status == EmStatus.Enabled && (ExpireTime is null || ExpireTime.Value > now.UtcDateTime);

        /// <summary>
        /// 是否试用
        /// </summary>
        public bool IsTrialUse { get; set; }

        /// <summary>
        /// 购买制度
        /// </summary>
        public EmPurchaseMode PurchaseMode { get; set; }

        /// <summary>
        /// 租户状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人ID
        /// </summary>
        public long? UpdatedBy { get; set; }

        /// <summary>
        /// 是否软删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTimeOffset? DeletedAt { get; set; }
    }
}