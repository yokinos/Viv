using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 组织表
    /// </summary>
    public class AtOrg : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 父级组织Id，顶级组织为0
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 组织名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 组织唯一编码
        /// </summary>
        [StringLength(64)]
        public string? OrgCode { get; set; }

        /// <summary>
        /// 组织层级深度，平台=0，一级代理=1，二级代理=2
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 全路径Id，逗号分隔，用于快速递归查询上级链路
        /// 例：0,10,25 代表 平台(0)→一级代理(10)→当前二级代理(25)
        /// </summary>
        [StringLength(1000)]
        public string? OrgPath { get; set; }

        /// <summary>
        /// 组织Logo地址
        /// </summary>
        [StringLength(800)]
        public string? LogoUrl { get; set; }

        /// <summary>
        /// 组织状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 组织类型：0-Viv自有 1-OEM
        /// </summary>
        public EmOrgType OrgType { get; set; }

        /// <summary>
        /// 备注说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人账号ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人账号ID
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