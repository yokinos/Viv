using System;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 组织应用关联表
    /// 配置该组织整体可使用的客户端应用，包含组织专属定制App
    /// </summary>
    public class AtOrgAppRelation : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 组织Id（关联AtOrg.Id）
        /// </summary>
        public long OrgId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 更新人
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