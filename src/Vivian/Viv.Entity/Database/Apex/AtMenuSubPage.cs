using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单子页面（依附 <see cref="AtMenu"/>，纯路由
    /// </summary>
    public class AtMenuSubPage : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 上级菜单Id（关联 AtMenu.Id）
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 排序号，数字越小越靠前
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 是否在页签/子导航里展示
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 子页面的客户端路由配置（JSON），与 <see cref="AtMenu.RouteConfig"/> 同一套约定
        /// </summary>
        public string? RouteConfig { get; set; }

        /// <summary>
        /// 备注（仅后台使用）
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态：启用 / 禁用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）
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
