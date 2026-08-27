using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单子页面表（对应前端路由 children 子路由）
    /// 依附主菜单 AtMenu.Id，存储目录下所有子页面路由
    /// </summary>
    public class AtMenuSubPage : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 上级主菜单 Id（关联 AtMenu.Id）
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 位索引（权限位运算）
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 子路由唯一名称（对应 Vue Router 的 name 字段）
        /// </summary>
        [StringLength(100)]
        public string? RouteName { get; set; }

        /// <summary>
        /// 页面展示名称（对应 meta.title）
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 子页面路由 path（相对路径，例：base / filter）
        /// </summary>
        [StringLength(500)]
        public string? Path { get; set; }

        /// <summary>
        /// 前端页面组件地址（对应 component，例：/list/base/index）
        /// </summary>
        [StringLength(500)]
        public string? Component { get; set; }

        /// <summary>
        /// 排序号（对应 meta.orderNo）
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 是否页面缓存（对应 meta.keepAlive）
        /// </summary>
        public bool IsKeepAlive { get; set; }

        /// <summary>
        /// 是否侧边栏显示（对应 meta.hidden 的反向）
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 备注描述（仅后台使用）
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态：0-禁用 / 1-启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人 ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人 ID
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