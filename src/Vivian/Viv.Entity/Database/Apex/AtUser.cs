using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    public class AtUser : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 用户类型
        /// </summary>
        public EmUserType UserType { get; set; }

        /// <summary>
        /// 所属组织Id，关联AtOrg.Id
        /// </summary>
        public long? OrgId { get; set; }

        /// <summary>
        /// 所属机构Id，关联AtTenant.Id
        /// </summary>
        public long? TenantId { get; set; }

        /// <summary>
        /// 用户名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        public string? NickName { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// 电话
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 盐
        /// </summary>
        public string? Salt { get; set; }

        /// <summary>
        /// 性别
        /// </summary>
        public EmGender? Gender { get; set; }

        /// <summary>
        /// 生日
        /// </summary>
        public DateTime? BirthDate { get; set; }

        /// <summary>
        /// 是否是超级管理员 此标记将获取对应的最大授权数据
        /// </summary>
        public bool IsSuperAdmin { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

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
    }
}
