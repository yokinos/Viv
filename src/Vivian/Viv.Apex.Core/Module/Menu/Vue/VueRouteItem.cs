using System;
using System.Collections.Generic;

namespace Viv.Apex.Core.Module.Menu.Vue
{
    /// <summary>
    /// Vue Router 路由节点。只有 Vue 端用，别的技术栈有自己的节点类型，不共享这个类
    /// </summary>
    public class VueRouteItem
    {
        /// <summary>
        /// 路由路径，与父节点的 path 组成最终地址；外部链接可以是 http 开头的地址
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 路由名称，页面缓存（keep-alive）按它匹配
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 渲染该路由的组件标识，目录固定 LAYOUT
        /// </summary>
        public string? Component { get; set; }

        /// <summary>
        /// 重定向地址
        /// </summary>
        public string? Redirect { get; set; }

        /// <summary>
        /// 路由元信息，主要是菜单展示相关
        /// </summary>
        public VueRouteMeta? Meta { get; set; }

        /// <summary>
        /// 子路由（子菜单、纯路由、子页面都挂这里）
        /// </summary>
        public List<VueRouteItem> Children { get; set; } = [];
    }
}
