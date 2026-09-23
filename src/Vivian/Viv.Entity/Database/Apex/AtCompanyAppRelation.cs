using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 公司应用关联表
    /// 在组织允许的App范围内，管控单个集团可用应用
    ///
    /// 三个掩码列存的是十进制数字符串（BigInteger 的 ToString），不是 long ——
    /// 权限位不受 64 位限制，读写走 BitIndexMaskMagic 的 FromText / ToText。
    /// </summary>
    public class AtCompanyAppRelation : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 公司Id（关联AtCompany.Id）
        /// </summary>
        public long CompanyId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 该公司此App允许开放的菜单最大权限掩码
        /// </summary>
        public string? MenuMask { get; set; }

        /// <summary>
        /// 该公司此App允许开放的子页面最大权限掩码
        /// </summary>
        public string? SubPageMask { get; set; }

        /// <summary>
        /// 该公司此App允许开放的按钮最大权限掩码
        /// </summary>
        public string? ButtonMask { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人ID
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
