using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    public enum EmUserType : byte
    {
        /// <summary>
        /// 运营平台用户
        /// </summary>
        Master = 0,

        /// <summary>
        /// 组织用户
        /// </summary>
        OrgUser = 1,

        /// <summary>
        /// 公司用户
        /// </summary>
        CompanyUser = 2,

        /// <summary>
        /// 租户用户
        /// </summary>
        TenantUser = 3,
 
        /// <summary>
        /// C端用户
        /// </summary>
        EndUser = 10,
    }
}
