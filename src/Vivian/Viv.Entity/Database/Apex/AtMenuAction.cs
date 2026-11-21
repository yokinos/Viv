using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单动作表（权限点）
    /// 一对多关联 AtMenu 页面菜单，一个页面多条操作动作
    /// </summary>
    public class AtMenuAction : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 所属页面菜单Id（仅关联Type=Page的菜单）
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 位索引，<b>全局自增</b>（不是每个菜单从 0 开始）。
        ///
        /// <para>为什么必须全局唯一：角色的授权是「一个角色一个掩码」（<c>AtUserRole.ButtonMask</c>），
        /// 里面没有菜单维度。如果两个菜单下的动作都占第 0 位，给角色授了其中一个，
        /// 另一个也自动被授 —— 那是提权，不是显示问题。</para>
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 动作名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 动作编码（如 <c>user:create</c>），服务端 <c>[VivRequirePermission]</c> 按它判定，与 BitIndex 无关。
        ///
        /// <para><b>为什么不能用 BitIndex 当标识</b>：它是 <c>max+1</c> 算出来的自增值，
        /// 开发库和生产库不一致，菜单动作增删一轮还会复用。<b>源码里只能写这个编码。</b></para>
        ///
        /// <para>约定：<c>模块:操作</c>，全局唯一（一个服务只对一套应用提供 API，跨应用重名请自己加前缀）。
        /// 为空 = 这个权限点只能用于前端按钮显隐，服务端按编码查不到它。</para>
        /// </summary>
        [StringLength(128)]
        public string? ActionCode { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 动作备注说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 启用状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人ID
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
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTimeOffset? DeletedAt { get; set; }
    }
}