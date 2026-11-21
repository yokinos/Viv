using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 授权点数变动流水（只增不改，<b>没有软删除</b>）。
    ///
    /// <para><b>为什么既要 <c>AtOrgSystemVersion.UsedPoints</c> 又要这张表</b>：
    /// 余额列用于「带条件的原子扣减」（防止并发超卖），流水用于对账与解释。
    /// 余额是快照、流水是事实；少了余额列就得每次 sum 全表才能判断能不能卖，
    /// 少了流水就无法回答「这 37 点是谁在什么时候卖给了谁」—— 续费纠纷和审计都卡在这。</para>
    ///
    /// <para><b>写入规则</b>：余额变更与流水插在<b>同一个事务</b>里。流水永远不改不删，
    /// 撤销授权就再记一条 <see cref="EmPointChangeType.Refund"/>，别去改历史行。</para>
    ///
    /// <para><see cref="OrgId"/> / <see cref="SystemVersionId"/> 是从授权行冗余过来的：
    /// Momo 的仓储按单表查，不冗余就得先查授权行再查流水，按代理/版本出报表时更麻烦。</para>
    /// </summary>
    public class AtOrgSystemVersionPointLog : EntityBase, ICreatedAt, ICreatedBy
    {
        /// <summary>
        /// 授权行Id（关联 AtOrgSystemVersion.Id）
        /// </summary>
        public long OrgSystemVersionId { get; set; }

        /// <summary>
        /// 组织Id（关联 AtOrg.Id），冗余自授权行，便于按代理出账
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 版本Id（关联 AtSystemVersion.Id），冗余自授权行
        /// </summary>
        public long SystemVersionId { get; set; }

        /// <summary>
        /// 变动类型。0 不是合法值（见 <see cref="EmPointChangeType"/>）
        /// </summary>
        public EmPointChangeType ChangeType { get; set; }

        /// <summary>
        /// 变动量。<b>正数=入池</b>（充值 / 退回），<b>负数=出池</b>（售卖扣减）。
        /// 存带符号的量而不是「绝对值 + 类型」：sum(Delta) 就是当前余额，
        /// 对账时能和 <c>UsedPoints</c> 逐行对上。
        /// </summary>
        public int Delta { get; set; }

        /// <summary>
        /// 本次变动后的剩余点数。存快照是为了对账时不必重放全量流水，
        /// 也能一眼看出「哪一步算错了」。
        /// </summary>
        public int BalanceAfter { get; set; }

        /// <summary>
        /// 卖给哪个租户（C 端/租户客户）。非售卖类变动为 null
        /// </summary>
        public long? TenantId { get; set; }

        /// <summary>
        /// 卖给哪个公司（集团客户）。非售卖类变动为 null
        /// </summary>
        public long? CompanyId { get; set; }

        /// <summary>
        /// 备注：合同号、批次、退回原因、人工调整的审批单号等
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间（流水的时间轴就是它）
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 操作人用户ID（平台运营或代理的操作账号）
        /// </summary>
        public long? CreatedBy { get; set; }
    }
}
