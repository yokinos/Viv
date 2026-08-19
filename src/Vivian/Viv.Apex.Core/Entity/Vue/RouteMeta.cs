using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Vue
{
    /// <summary>
    /// 路由Meta元数据，控制菜单展示信息
    /// </summary>
    public class RouteMeta
    {
        /// <summary>
        /// 该路由在菜单上展示的标题
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 该路由在菜单上展示的图标
        /// </summary>
        public string? Icon { get; set; }

        /// <summary>
        /// 决定该路由在菜单上是否默认展开
        /// </summary>
        public bool? Expanded { get; set; }

        /// <summary>
        /// 该路由在菜单上展示先后顺序，数字越小越靠前，默认为零
        /// </summary>
        public int? OrderNo { get; set; }

        /// <summary>
        /// 决定该路由是否在菜单上进行展示
        /// </summary>
        public bool? Hidden { get; set; }

        /// <summary>
        /// 如果启用了面包屑，决定该路由是否在面包屑上进行展示
        /// </summary>
        public bool? HiddenBreadcrumb { get; set; }

        /// <summary>
        /// 如果是多级菜单且只存在一个节点，想在菜单上只展示一级节点，可以使用该配置。请注意该配置需配置在父节点
        /// </summary>
        public bool? Single { get; set; }

        /// <summary>
        /// 内嵌 iframe 的地址
        /// </summary>
        public string? FrameSrc { get; set; }

        /// <summary>
        /// 内嵌 iframe 的地址是否以新窗口打开
        /// </summary>
        public bool? FrameBlank { get; set; }

        /// <summary>
        /// 可决定路由是否开启keep‑alive，默认开启
        /// </summary>
        public bool? KeepAlive { get; set; }
    }
}
