using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    public class AtUser : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 所属组织Id，关联AtOrg.Id
        /// </summary>
        public long OrgId { get; set; }

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
        public string? Avatar { get; set; }

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
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}
