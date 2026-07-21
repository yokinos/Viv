using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单按钮表
    /// 一对多关联 AtMenu 页面菜单，一个页面多条操作按钮
    /// </summary>
    public class AtMenuButton : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 所属页面菜单Id（仅关联Type=Page的菜单）
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 位索引，菜单类型独立自增
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 按钮名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 按钮备注说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 启用状态
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