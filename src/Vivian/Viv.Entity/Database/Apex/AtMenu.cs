using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单主表（对应前端路由）
    /// 按 ClientAppId 隔离不同客户端应用菜单，全局顶层表无租户隔离
    /// </summary>
    public class AtMenu : EntityBase, ISoftDeleted
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
        /// 父菜单Id，顶级菜单为 0
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 前端路由 name（唯一标识，对应 Vue Router 的 name 字段）
        /// </summary>
        [StringLength(100)]
        public string? RouteName { get; set; }

        /// <summary>
        /// 菜单显示名称（对应 meta.title）
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 前端路由路径 / 外链地址（对应 path）
        /// </summary>
        [StringLength(500)]
        public string? Path { get; set; }

        /// <summary>
        /// 前端组件标识（对应 component）
        /// 目录级固定为 "LAYOUT"，页面级为具体组件路径
        /// </summary>
        [StringLength(500)]
        public string? Component { get; set; }

        /// <summary>
        /// 重定向路由地址（对应 redirect）
        /// </summary>
        [StringLength(500)]
        public string? Redirect { get; set; }

        /// <summary>
        /// 菜单图标（对应 meta.icon）
        /// </summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>
        /// 排序号（对应 meta.orderNo，数字越小越靠前）
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 菜单类型：0-目录 / 1-页面
        /// </summary>
        public EmMenuType Type { get; set; }

        /// <summary>
        /// 菜单描述（仅后台使用，不输出到前端）
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否为外部链接（对应 meta.frameSrc）
        /// </summary>
        public bool IsExternalLink { get; set; }

        /// <summary>
        /// 外部链接是否新标签页打开（对应 meta.frameBlank）
        /// </summary>
        public bool IsOpenNewTab { get; set; }

        /// <summary>
        /// 路由页面是否持久缓存（对应 meta.keepAlive）
        /// </summary>
        public bool IsKeepAlive { get; set; }

        /// <summary>
        /// 是否侧边栏显示（对应 meta.hidden 的反向）
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 菜单状态：0-禁用 / 1-启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 是否默认展开（对应 meta.expanded）
        /// </summary>
        public bool IsExpanded { get; set; }

        /// <summary>
        /// 是否为单级菜单（父节点只有一个子节点时，菜单只展示一级）
        /// </summary>
        public bool IsSingle { get; set; }

        /// <summary>
        /// 是否在面包屑中隐藏（对应 meta.hiddenBreadcrumb）
        /// </summary>
        public bool IsHiddenBreadcrumb { get; set; }

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