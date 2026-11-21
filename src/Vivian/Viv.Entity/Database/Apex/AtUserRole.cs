using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 角色表。
    ///
    /// <para><b>权限不在这里</b>：角色的菜单/操作掩码挂在 <see cref="AtUserRoleApp"/> 上，
    /// 按「角色 × 应用」存 <b>一行一套掩码</b>。一个角色可以跨多个应用（同一岗位在不同端的
    /// 权限不一样），而权限位是每个应用从 0 分配，所以掩码必须带应用维度 ——
    /// 挂在本表上就是「两个应用的同一位置互相授权」。</para>
    /// </summary>
    public class AtUserRole : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 角色名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 角色唯一编码
        /// </summary>
        [StringLength(64)]
        public string? RoleCode { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        public EmUserType UserType { get; set; }

        /// <summary>
        /// 角色备注
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态 0禁用 1启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人用户ID
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人用户ID
        /// </summary>
        public long? UpdatedBy { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTimeOffset? DeletedAt { get; set; }
    }
}