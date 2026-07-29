using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单主表（对应前端路由目录/一级页面）
    /// 按ClientAppId隔离不同客户端应用菜单，全局顶层表无租户隔离
    /// </summary>
    public class AtMenu : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 所属客户端应用Id（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 位索引，菜单类型独立自增
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 父菜单Id，顶级菜单为0
        /// </summary>
        public long ParentId { get; set; }

        /// <summary>
        /// 前端路由name（唯一标识，对应路由name字段，例：list）
        /// </summary>
        [StringLength(100)]
        public string? RouteName { get; set; }

        /// <summary>
        /// 菜单名称（meta.title，展示侧边栏文字）
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 前端路由路径 / 外链地址（对应path）
        /// </summary>
        [StringLength(500)]
        public string? Path { get; set; }

        /// <summary>
        /// 前端组件标识（对应component，例：LAYOUT、/list/base/index）
        /// </summary>
        [StringLength(500)]
        public string? Component { get; set; }

        /// <summary>
        /// 重定向路由地址（对应redirect）
        /// </summary>
        [StringLength(500)]
        public string? Redirect { get; set; }

        /// <summary>
        /// 菜单图标（meta.icon）
        /// </summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>
        /// 排序号
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 菜单类型：0目录/1页面/2按钮
        /// </summary>
        public byte Type { get; set; }

        /// <summary>
        /// 菜单描述
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否为外部链接
        /// </summary>
        public bool IsExternalLink { get; set; }

        /// <summary>
        /// 是否新标签页打开
        /// </summary>
        public bool IsOpenNewTab { get; set; }

        /// <summary>
        /// 路由页面是否持久缓存（meta.keepAlive）
        /// </summary>
        public bool IsKeepAlive { get; set; }

        /// <summary>
        /// 菜单状态 0禁用 1启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 是否侧边栏显示（meta.hidden反向）
        /// </summary>
        public bool IsVisible { get; set; }

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