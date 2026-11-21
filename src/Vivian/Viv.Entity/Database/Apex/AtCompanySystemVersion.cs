using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 公司购买的版本数量
    /// </summary>
    public class AtCompanySystemVersion : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 公司Id（关联 AtCompany.Id）
        /// </summary>
        public long CompanyId { get; set; }

        /// <summary>
        /// 版本Id（关联 <see cref="AtSystemVersion.Id"/>）
        /// </summary>
        public long SystemVersionId { get; set; }

        /// <summary>
        /// 代理批给该公司的总授权点数（1 点 = 1 个「授权年」，即卖一套一年期扣 1 点）。
        /// </summary>
        public int TotalPoints { get; set; }

        /// <summary>
        /// 已用点数。剩余 = <c>TotalPoints - UsedPoints</c>（不存派生列）。
        /// 退回授权时减回去，并在 <see cref="AtCompanySystemVersionPointLog"/> 留一条流水。
        /// </summary>
        public int UsedPoints { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 备注（代理发货说明：合同号、批次、特批原因等）
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
