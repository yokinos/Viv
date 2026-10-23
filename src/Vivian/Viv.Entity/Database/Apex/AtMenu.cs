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
        /// 位索引，菜单类型独立自增（用于权限位运算）
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
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

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
        public DateTime? DeletedAt { get; set; }
    }
}
