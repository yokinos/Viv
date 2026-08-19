using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Vue
{
    /// <summary>
    /// 适配Vue的路由项
    /// </summary>
    public class RouteItem
    {
        /// <summary>
        /// 当前路由的路径，会与配置中的父级节点的 path 组成该页面路由的最终路径；如果需要跳转外部链接，可以将path设置为 http 协议开头的路径。
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 路由name，影响多标签 Tab 页的 keep‑alive 的能力，如果要确保页面有 keep‑alive 的能力，请保证该路由的name与对应页面（SFC)的name保持一致。
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 渲染该路由时使用的页面组件，LAYOUT代表布局外壳组件
        /// </summary>
        public string? Component { get; set; }

        /// <summary>
        /// 重定向的路径
        /// </summary>
        public string? Redirect { get; set; }

        /// <summary>
        /// 路由元信息，主要用途是路由在菜单上展示的效果的配置
        /// </summary>
        public RouteMeta? Meta { get; set; }

        /// <summary>
        /// 子菜单的配置
        /// </summary>
        public List<RouteItem>? Children { get; set; }
    }
}
