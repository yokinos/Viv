using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    public class AtUserRoleRelation : EntityBase
    {
        /// <summary>
        /// 用户Id AtUser.Id
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 角色Id  AtUserRole.Id
        /// </summary>
        public long RoleId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人用户ID
        /// </summary>
        public long? CreatedBy { get; set; }
    }
}
