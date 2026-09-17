using System;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 租户应用关联表
    /// 在组织允许的App范围内，精细化管控单个租户可用应用
    /// </summary>
    public class AtTenantAppRelation : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 租户Id（关联AtTenant.Id）
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 该租户此App允许开放的菜单最大权限掩码
        /// </summary>
        public ulong MenuMask { get; set; }

        /// <summary>
        /// 该租户此App允许开放的子页面最大权限掩码
        /// </summary>
        public ulong SubPageMask { get; set; }

        /// <summary>
        /// 该租户此App允许开放的按钮最大权限掩码
        /// </summary>
        public ulong ButtonMask { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人ID
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}