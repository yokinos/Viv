using System;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 组织应用关联表
    /// 1. 控制组织可上架/使用哪些客户端App
    /// 2. 配套三组最大功能掩码，限制该OEM组织售卖套餐的功能上限
    /// </summary>
    public class AtOrgAppRelation : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 组织Id（关联AtOrg.Id）
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 该组织此App允许开放的菜单最大权限掩码（售卖天花板）
        /// </summary>
        public ulong MaxMenuMask { get; set; }

        /// <summary>
        /// 该组织此App允许开放的子页面最大权限掩码（售卖天花板）
        /// </summary>
        public ulong MaxSubPageMask { get; set; }

        /// <summary>
        /// 该组织此App允许开放的按钮最大权限掩码（售卖天花板）
        /// </summary>
        public ulong MaxButtonMask { get; set; }

        /// <summary>
        /// 本条App权限启用状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

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
        public DateTime? DeletedAt { get; set; }
    }
}