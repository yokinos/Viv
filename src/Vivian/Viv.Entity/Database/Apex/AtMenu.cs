using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    public class AtMenu : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 菜单名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 菜单路径
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 菜单图标
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// 菜单排序
        /// </summary>
        public int? Sort { get; set; }

        /// <summary>
        /// 菜单类型
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// 菜单描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 父菜单ID
        /// </summary>
        public int? ParentId { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 是否显示
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public string? UpdatedBy { get; set; }
    }
}
