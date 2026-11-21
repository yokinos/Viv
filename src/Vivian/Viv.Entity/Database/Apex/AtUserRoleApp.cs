using System;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 角色 × 客户端应用 —— 角色的授权就是这个维度的。
    ///
    /// <para><b>为什么必须有这张表</b>：一个角色可以跨多个应用（「店长」在后端管理端和门店端
    /// 是不同的权限集合），而权限位是<b>按应用分配</b>的（<c>AtMenu.BitIndex</c> 每个应用从 0 自增）。
    /// 把掩码挂在角色上，等于强行给一个跨应用的实体配一个没有应用维度的掩码 ——
    /// 两个应用的第 3 位会互相授权，那是提权，不是显示问题。</para>
    ///
    /// <para><b>形状与 <see cref="AtSystemVersionAppRelation"/> 一致</b>（版本也是「一套版本 ×
    /// 多个应用、每个应用一套掩码」），只是那边授的是「版本包含什么」，这边授的是「角色能做什么」。</para>
    ///
    /// <para><b>行存在 = 这个角色被分配到了该应用</b>。已经分配但还没勾权限就存 0 掩码，
    /// 与「没分配」区分开 —— 后台的「应用多选」和「权限树」是一一对应的。</para>
    ///
    /// <para>读写一律走 <c>BitIndexMaskMagic</c>（<c>FromText</c>/<c>ToText</c>/<c>Combine</c>/<c>Has</c>），
    /// 空值当 0（无权限位）。唯一索引 (RoleId, ClientAppId) 要手工建，`SchemaSynchronizer` 不生成索引：
    /// <c>CREATE UNIQUE INDEX UX_AtUserRoleApp ON AtUserRoleApp (RoleId, ClientAppId) WHERE IsDeleted = 0;</c></para>
    /// </summary>
    public class AtUserRoleApp : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 角色Id（关联 AtUserRole.Id）
        /// </summary>
        public long RoleId { get; set; }

        /// <summary>
        /// 客户端应用Id（关联 AtClientApp.Id）。多端就是不同的应用，所以这是「哪一端的权限」
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 页面权限掩码：十进制存 <c>BigInteger</c>，位下标 = 该应用下的 <c>AtMenu.BitIndex</c>。
        /// 控制「这个角色在本应用能看哪些页面」，配合前端路由显隐。
        /// </summary>
        public string? MenuMask { get; set; }

        /// <summary>
        /// 操作权限掩码：十进制存 <c>BigInteger</c>，位下标 = 该应用下的 <c>AtMenuAction.BitIndex</c>。
        /// 服务端 <c>[VivRequirePermission]</c> 判的就是它（用户全部启用角色的本应用行按位或）。
        /// </summary>
        public string? ButtonMask { get; set; }

        /// <summary>
        /// 状态：禁用 = 这个角色在本应用下不再授予任何权限（角色本身可能还启用着别的应用）
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
        public DateTimeOffset? DeletedAt { get; set; }
    }
}
