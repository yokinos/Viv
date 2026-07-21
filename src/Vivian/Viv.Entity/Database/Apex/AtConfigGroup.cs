using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Base;
using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 配置分组字典表
    /// 区分全局配置分组 / App专属配置分组，统一维护分组编码与名称
    /// </summary>
    public class AtConfigGroup : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 分组归属类型：Global全局 / App应用
        /// </summary>
        public EmConfigGroupBindType BindType { get; set; }

        /// <summary>
        /// 分组展示名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 分组说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        public EmStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public long? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}