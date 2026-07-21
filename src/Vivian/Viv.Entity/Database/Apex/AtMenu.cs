using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 菜单表
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
        /// 菜单名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 前端路由路径 / 外链地址
        /// </summary>
        [StringLength(500)]
        public string? Path { get; set; }

        /// <summary>
        /// 菜单图标
        /// </summary>
        [StringLength(500)]
        public string? Icon { get; set; }

        /// <summary>
        /// 排序号
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 菜单类型：目录/页面/按钮
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
        /// 路由页面是否持久缓存
        /// </summary>
        public bool IsKeepAlive { get; set; }

        /// <summary>
        /// 菜单状态 0禁用 1启用
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 是否侧边栏显示
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