using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单节点（可以是一级/多级菜单，也可以是不出现在菜单里的纯路由）
    /// 按 ClientAppId 隔离：同一应用的不同平台是不同的 AppId，所以路由配置天然按端分开
    /// </summary>
    public class AtMenu : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 所属客户端应用Id（关联 AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 位索引，菜单类型独立自增（用于权限位运算）
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 父节点Id，顶级为 0
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 图标标识，具体字体/图片由各端自己解释
        /// </summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>
        /// 排序号，数字越小越靠前
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 节点类型：目录 / 菜单页 / 纯路由
        /// </summary>
        public EmMenuType Type { get; set; }

        /// <summary>
        /// 是否在菜单里展示。纯路由（Type=Route）不看这个字段，一律不展示
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 客户端路由配置（JSON）。各端自己解释里面的键，后端只负责存取和透传
        /// </summary>
        public string? RouteConfig { get; set; }

        /// <summary>
        /// 菜单描述（仅后台使用，不输出到前端）
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 状态：启用 / 禁用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

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
        public DateTime? DeletedAt { get; set; }
    }
}
