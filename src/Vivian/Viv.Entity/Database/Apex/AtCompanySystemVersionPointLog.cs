using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 公司授权点数变动流水（只增不改，<b>没有软删除</b>）。
    /// </summary>
    public class AtCompanySystemVersionPointLog : EntityBase, ICreatedAt, ICreatedBy
    {
        /// <summary>
        /// 授权行Id（关联 AtCompanySystemVersion.Id）
        /// </summary>
        public long CompanySystemVersionId { get; set; }

        /// <summary>
        /// 公司Id（关联 AtCompany.Id），冗余自授权行，便于按公司出账
        /// </summary>
        public long CompanyId { get; set; }

        /// <summary>
        /// 版本Id（关联 AtSystemVersion.Id），冗余自授权行
        /// </summary>
        public long SystemVersionId { get; set; }

        /// <summary>
        /// 变动类型。0 不是合法值（见 <see cref="EmPointChangeType"/>）
        /// </summary>
        public EmPointChangeType ChangeType { get; set; }

        /// <summary>
        /// 变动量。<b>正数=入池</b>（代理划拨 / 退回），<b>负数=出池</b>（卖给租户扣减）。
        /// <c>sum(Delta)</c> 就是当前余额，能与 <c>UsedPoints</c> 逐行对上。
        /// </summary>
        public int Delta { get; set; }

        /// <summary>
        /// 本次变动后的剩余点数。存快照，对账时不必重放全量流水。
        /// </summary>
        public int BalanceAfter { get; set; }

        /// <summary>
        /// 卖给哪个租户。非售卖类变动为 null
        /// </summary>
        public long? TenantId { get; set; }

        /// <summary>
        /// 备注：代理发货单号、退回原因、人工调整的审批单等
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间（流水的时间轴就是它）
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 操作人用户ID（代理或公司的操作账号）
        /// </summary>
        public long? CreatedBy { get; set; }
    }
}
