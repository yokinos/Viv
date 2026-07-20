using System;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 套餐与客户端应用关联表
    /// 一套版本可绑定多个App，每个App独立配置一套功能掩码
    /// </summary>
    public class AtSystemVersionAppRelation : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 套餐主表Id（关联AtSystemVersion.Id）
        /// </summary>
        public long SystemVersionId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 当前App对应的主菜单权限掩码
        /// </summary>
        public ulong MenuMask { get; set; }

        /// <summary>
        /// 当前App对应的子页面权限掩码
        /// </summary>
        public ulong SubPageMask { get; set; }

        /// <summary>
        /// 当前App对应的操作按钮权限掩码
        /// </summary>
        public ulong ButtonMask { get; set; }

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