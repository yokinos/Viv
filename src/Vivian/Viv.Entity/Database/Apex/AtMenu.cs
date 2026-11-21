using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单节点（目录或页面）。详情页、编辑页这类只在代码里跳转的页面挂在 AtMenuSubPage 上，
    /// 用 IsVisible=false 表达"不进菜单"，不占用菜单节点。
    /// 按 ClientAppId 隔离：同一应用的不同平台是不同的 AppId，所以路由配置天然按端分开
    /// </summary>
    public class AtMenu : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 所属客户端应用Id（关联 AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 位索引，菜单类型独立自增（用于权限位运算）。
        ///
        /// <para><b>注意</b>：这里是「每个应用从 0 自增」，而角色的页面权限掩码
        /// （<c>AtUserRole.MenuMask</c>）没有应用维度 —— 一旦真按页面做跨应用授权，
        /// 不同应用的同号页面会互相授权。届时需要改成全局唯一（动作位已经改了）。</para>
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 父节点Id，顶级为 0
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 菜单编码（如 <c>user</c>），页面级权限的稳定标识，服务端
        /// <c>[VivRequirePermission("user")]</c> 按它判定，与 BitIndex 无关。
        ///
        /// <para>约定：不带冒号，全局唯一。<b>目录和页面都能编</b> —— 给目录编一个就是
        /// 「模块级权限」（有这个模块就能进），给页面编就是「页面级」。
        /// 动作的编码另有一套（<c>user:add</c>，带冒号），两套编码不能重名。</para>
        ///
        /// <para>为空 = 这个菜单只能靠角色掩码里的位表达，源码里引用不到。</para>
        /// </summary>
        [StringLength(128)]
        public string? MenuCode { get; set; }

        /// <summary>
        /// 图标标识，具体字体/图片由各端自己解释
        /// </summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>
        /// 排序号，数字越小越靠前
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 节点类型：目录 / 页面
        /// </summary>
        public EmMenuType Type { get; set; }

        /// <summary>
        /// 是否在菜单里展示。隐藏的菜单项仍然会注册路由，只是侧边栏看不到
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 客户端路由配置（JSON）。约定是「一个路由节点的形状」，由各端适配器解释；
        /// 展示信息（标题/图标/排序/可见性）以列上的值为准，children 由层级生成，JSON 里写了也不生效
        /// </summary>
        public string? RouteConfig { get; set; }

        /// <summary>
        /// 菜单描述（仅后台使用，不输出到前端）
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 状态：启用 / 禁用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）
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
