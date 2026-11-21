using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 系统售卖版本主表（平台统一制定）。
    ///
    /// <para><b>版本只由运营平台定义</b>，代理商不能自己造版本 —— 要定制就向平台购买新版本。
    /// 代理能卖哪些版本由 <see cref="AtOrgSystemVersion"/> 决定，能卖到什么程度由
    /// <c>AtOrgAppRelation.Max*Mask</c>（售卖天花板）兜底：售卖时
    /// <b>授权掩码 = 版本掩码 AND 代理上限</b>，两头都卡住才叫控制。</para>
    ///
    /// <para><b>版本不可原地修改</b>：改功能就新建版本、把旧版本 <c>Status</c> 置停用。
    /// 客户权限是售卖时写进 <c>AtCompany/TenantAppRelation</c> 的掩码快照，不受版本变动影响；
    /// 但版本编码会进合同与续费记录，原地改会让「卖给客户的是什么」失去可追溯性。</para>
    ///
    /// <para>仅存储套餐基础信息，各 App 独立权限存放于 <see cref="AtSystemVersionAppRelation"/>。</para>
    /// </summary>
    public class AtSystemVersion : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 版本套餐名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 套餐唯一编码（如 <c>basic</c> / <c>pro-2026</c>），全局唯一。
        /// 合同、续费、工单都按它对齐 —— 一旦发出不要再改。
        /// </summary>
        [StringLength(64)]
        public string? Code { get; set; }

        /// <summary>
        /// 建议零售价（单位：分）。<b>不是成交价</b> —— 代理卖多卖少是商业条款，
        /// 实际价格走合同/订单，别往这张表里塞。
        /// </summary>
        public long Price { get; set; }

        /// <summary>
        /// 套餐有效期天数，0=永久有效
        /// </summary>
        public int ValidDays { get; set; }

        /// <summary>
        /// 套餐介绍
        /// </summary>
        [StringLength(2000)]
        public string? Description { get; set; }

        /// <summary>
        /// 套餐状态 0停用 1启用
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
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTimeOffset? DeletedAt { get; set; }
    }
}