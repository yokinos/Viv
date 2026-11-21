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
        /// 位索引，<b>每个应用内自增</b>（应用归属靠 <c>MenuId</c> → <see cref="AtMenu.ClientAppId"/> 反查）。
        ///
        /// <para>只在应用内唯一就够：角色的授权掩码是按「角色 × 应用」存的
        /// （<see cref="AtUserRoleApp.ButtonMask"/>），一个应用一套掩码，跨应用根本不会碰到同一位。
        /// 早先因为掩码挂在角色本体上、没有应用维度，这里被迫改成全局唯一来绕开「跨菜单同位互相授权」；
        /// 掩码补上应用维度之后，那个约束就没有存在意义了，位号回到应用内自增——
        /// 号段短、能在后台按应用人工核对。</para>
        ///
        /// <para>位号一经分配<b>不回收</b>（发号取的是含软删行的 MAX），否则删掉高位动作后新动作
        /// 复用同一个位，旧角色掩码会静默变成对新动作的授权。</para>
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 动作名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 权限类型（比如多个tab就是页面  新增就是按钮 ）
        /// </summary>
        public EmMenuActionType ActionType { get; set; }

        /// <summary>
        /// 动作编码 单个App下不能重复
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