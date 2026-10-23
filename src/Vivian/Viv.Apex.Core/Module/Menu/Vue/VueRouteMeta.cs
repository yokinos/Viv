using System;
using System.Collections.Generic;

namespace Viv.Apex.Core.Module.Menu.Vue
{
    /// <summary>Vue 路由的 meta。差异项允许为空，由适配器给默认值</summary>
    public class VueRouteMeta
    {
        /// <summary>菜单标题</summary>
        public string? Title { get; set; }

        /// <summary>菜单图标</summary>
        public string? Icon { get; set; }

        /// <summary>是否默认展开</summary>
        public bool? Expanded { get; set; }

        /// <summary>展示顺序，数字越小越靠前</summary>
        public int? OrderNo { get; set; }

        /// <summary>是否隐藏（纯路由、或者 IsVisible=false 的节点为 true）</summary>
        public bool? Hidden { get; set; }

        /// <summary>面包屑里是否隐藏</summary>
        public bool? HiddenBreadcrumb { get; set; }

        /// <summary>多级菜单只剩一个子节点时，是否只展示一级</summary>
        public bool? Single { get; set; }

        /// <summary>内嵌 iframe 的地址</summary>
        public string? FrameSrc { get; set; }

        /// <summary>iframe 是否新窗口打开</summary>
        public bool? FrameBlank { get; set; }

        /// <summary>是否开启页面缓存</summary>
        public bool? KeepAlive { get; set; }
    }
}
