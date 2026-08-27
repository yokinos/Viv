using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端App专属配置表
    /// 单App独立业务参数、开关配置，仅绑定ClientApp，与全局配置AtGlobalConfig区分
    /// </summary>
    public class AtClientAppSetting : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 所属客户端AppId
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 配置分组Id
        /// </summary>
        [StringLength(64)]
        public long GroupId { get; set; }

        /// <summary>
        /// 配置唯一标识Key
        /// </summary>
        [StringLength(128)]
        public string? ConfigKey { get; set; }

        /// <summary>
        /// 配置值，简单文本直接存，复杂对象存储JSON字符串
        /// </summary>
        public string? ConfigValue { get; set; }

        /// <summary>
        /// 配置项名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 配置说明
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