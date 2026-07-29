using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单子页面表（对应前端路由children子路由）
    /// 依附主菜单AtMenu.Id，存储目录下所有子页面路由
    /// </summary>
    public class AtMenuSubPage : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 上级主菜单Id（关联AtMenu.Id，对应外层路由）
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 位索引，菜单类型独立自增
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 子路由唯一名称（路由name，例：ListBase）
        /// </summary>
        [StringLength(100)]
        public string? RouteName { get; set; }

        /// <summary>
        /// 页面展示名称（meta.title）
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 子页面路由path（相对路径，例：base / filter）
        /// </summary>
        [StringLength(500)]
        public string? Path { get; set; }

        /// <summary>
        /// 前端页面组件地址（component，例：/list/base/index）
        /// </summary>
        [StringLength(500)]
        public string? Component { get; set; }

        /// <summary>
        /// 排序号（children页面展示顺序）
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 是否页面缓存（meta.keepAlive）
        /// </summary>
        public bool IsKeepAlive { get; set; }

        /// <summary>
        /// 备注描述
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态 0禁用 1启用
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

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}