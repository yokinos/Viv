using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 系统售卖版本主表
    /// 仅存储套餐基础信息，各App独立权限存放于AtSystemVersionAppRelation
    /// </summary>
    public class AtSystemVersion : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 版本套餐名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 套餐唯一编码
        /// </summary>
        [StringLength(64)]
        public string? Code { get; set; }

        /// <summary>
        /// 售卖套餐类型
        /// </summary>
        public EmSystemSaleType SaleType { get; set; }

        /// <summary>
        /// 套餐价格（单位：分）
        /// </summary>
        public long Price { get; set; }

        /// <summary>
        /// 套餐有效期天数，0=永久有效
        /// </summary>
        public int ValidDays { get; set; }

        /// <summary>
        /// 套餐介绍
        /// </summary>
        [StringLength(2000)]
        public string? Description { get; set; }

        /// <summary>
        /// 套餐状态 0停用 1启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

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