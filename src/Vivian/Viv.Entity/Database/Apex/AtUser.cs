using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    public class AtUser : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 用户类型
        /// </summary>
        public EmUserType UserType { get; set; }

        /// <summary>
        /// 所属组织Id，关联AtOrg.Id。归属链的最上层（大代理商）
        /// </summary>
        public long? OrgId { get; set; }

        /// <summary>
        /// 所属公司Id，关联AtCompany.Id。归属链的中间层
        /// </summary>
        public long? CompanyId { get; set; }

        /// <summary>
        /// 所属机构Id，关联AtTenant.Id。归属链的最下层
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
        /// 该账号在「当前用户类型」下的主体 Id：OrgUser → <see cref="OrgId"/>，
        /// CompanyUser → <see cref="CompanyId"/>，TenantUser / EndUser → <see cref="TenantId"/>。
        /// 平台账号不挂归属链，返回 0（它的主体由 <c>VivSubject.PlatformId</c> 固定表示，不在这一行上）。
        /// <para>
        /// 三列同时存在，是因为它们是<b>归属链</b>（组织 → 公司 → 机构），不是三选一：
        /// 一个公司用户既要知道属于哪家公司，也要知道那家公司属于哪个大代理商。
        /// 哪一列代表「主体」由 <see cref="UserType"/> 决定。
        /// </para>
        /// <para>
        /// 刻意写成静态方法而不是只读属性：<c>SchemaSynchronizer</c> 按 public 实例属性建列，
        /// 属性写法会被当成一个新列去同步。
        /// </para>
        /// </summary>
        public static long SubjectIdOf(AtUser user) => user.UserType switch
        {
            EmUserType.OrgUser => user.OrgId ?? 0,
            EmUserType.CompanyUser => user.CompanyId ?? 0,
            EmUserType.TenantUser or EmUserType.EndUser => user.TenantId ?? 0,
            _ => 0
        };
    }
}
