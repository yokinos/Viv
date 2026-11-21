using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 组织可售版本清单：平台把某个版本「发给」某个代理，代理才有资格卖它。
    ///
    /// <para><b>为什么单独一张表，而不是给版本加 OrgId</b>：版本归平台所有（一个版本可以
    /// 同时开放给多个代理），归属关系放在关联表里才不会让「版本」被某个代理独占。
    /// 以前是版本带 <c>OrgId</c>，等于每个代理各造一套版本，同一功能有 N 个版本号，
    /// 续费、对账、排障都对不齐。</para>
    ///
    /// <para><b>它只管「能卖什么」，不管「能卖到什么程度」</b>：后者由
    /// <c>AtOrgAppRelation.Max*Mask</c> 兜底。售卖时两个条件都过：
    /// ① 版本在本表里（代理有售权）；② 授出的掩码 = 版本掩码 AND 代理上限（不越界）。
    /// 少任何一条，代理都能把平台没开的功能卖出去。</para>
    ///
    /// <para>软删除一行 = 收回该代理对这个版本的售权（已售客户不受影响，他们的授权是掩码快照）。</para>
    ///
    /// <para><b>授权点数</b>：一行还代表「这个代理能卖多少套」。平台按点数批给代理
    /// （买 100 点 = 可卖 100 个「授权年」），卖一套一年期的客户扣 1 点。
    /// 扣减必须是<b>带条件的原子更新</b>（<c>UsedPoints + n &lt;= TotalPoints</c>），
    /// 否则并发售卖会超卖 —— 超卖没有技术后果，只有商务后果（平台得补货或毁约）。</para>
    ///
    /// <para>每一次变动都要在 <see cref="AtOrgSystemVersionPointLog"/> 里留一条流水，
    /// 与余额变更同一个事务：余额列负责「能不能卖」，流水负责「卖给谁、谁批的、为什么退」。</para>
    /// </summary>
    public class AtOrgSystemVersion : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 组织Id（关联 AtOrg.Id），即获得售权的代理
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 版本Id（关联 AtSystemVersion.Id）
        /// </summary>
        public long SystemVersionId { get; set; }

        /// <summary>
        /// 平台批给该代理的总授权点数（1 点 = 1 个「授权年」，即卖一套一年期扣 1 点）。
        ///
        /// <para>点数只在<b>授权落地</b>时扣：写 <c>AtCompany/TenantAppRelation</c> 掩码 + 设到期时间
        /// 的同一个事务里扣，失败一起回滚。别在下单先扣、授权失败再补 —— 补不回来就是纠纷。</para>
        /// </summary>
        public int TotalPoints { get; set; }

        /// <summary>
        /// 已用点数。剩余 = <c>TotalPoints - UsedPoints</c>（不存派生列，避免两处真值）。
        /// 退回授权时<b>减回去</b>，并在备注里写清原因；不要改历史流水，对账要能解释每一次变化。
        /// </summary>
        public int UsedPoints { get; set; }

        /// <summary>
        /// 备注（平台批点数时的说明：合同号、批次、特批原因等）
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人用户ID
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
