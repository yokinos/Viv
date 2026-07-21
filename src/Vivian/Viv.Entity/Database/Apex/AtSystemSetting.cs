using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>系统全局配置表
    /// 支持全局/组织/租户范围配置，关联配置分组Id
    /// </summary>
    public class AtSystemSetting : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 关联配置分组Id AtConfigGroup.Id
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// 配置唯一标识Key
        /// </summary>
        [StringLength(128)]
        public string? ConfigKey { get; set; }

        /// <summary>
        /// 绑定对象类型：Global全局 / Org组织 / Tenant租户
        /// </summary>
        public EmNoticeBindType BindType { get; set; }

        /// <summary>
        /// 绑定对象ID，Global固定0，Org存组织Id，Tenant存租户Id
        /// </summary>
        public long BindId { get; set; }

        /// <summary>
        /// 配置值
        /// </summary>
        public string? ConfigValue { get; set; }

        /// <summary>
        /// 配置名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

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