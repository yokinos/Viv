using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 角色表
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
        /// 页面权限掩码：十进制存 <c>BigInteger</c>，位下标 = <c>AtMenu.BitIndex</c>。
        /// 控制「这个角色能看哪些页面」，配合前端路由显隐；后端按页面挡不挡（见 <see cref="ButtonMask"/> 的说明）。
        ///
        /// <para><b>目前没有写入口，也没有读取方</b> —— 后端鉴权走操作权限。等真做页面鉴权时，
        /// 注意 <c>AtMenu.BitIndex</c> 现在是「每个应用从 0 自增」，而角色掩码没有应用维度，
        /// 跨应用的同号页面会互相授权，那时要把菜单位也改成全局唯一。</para>
        /// </summary>
        public string? MenuMask { get; set; }

        /// <summary>
        /// 操作权限掩码：十进制存 <c>BigInteger</c>，位下标 = <c>AtMenuAction.BitIndex</c>。
        /// 控制「这个角色能执行哪些动作」，服务端 <c>[VivRequirePermission]</c> 判的就是它。
        ///
        /// <para>不建角色-动作关联表：动作总量在几百的量级，位掩码一个字段就够；
        /// 判定时把用户全部启用角色的掩码按位或，再测目标位 —— 一次位与，不要 join。
        /// 前提是 <c>AtMenuAction.BitIndex</c> <b>全局唯一</b>（见那里的注释）。</para>
        ///
        /// <para>读写一律走 <c>BitIndexMaskMagic</c>（<c>FromText</c>/<c>ToText</c>/<c>Combine</c>/<c>Has</c>），
        /// 空值当 0（无权限位）。</para>
        /// </summary>
        public string? ButtonMask { get; set; }

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