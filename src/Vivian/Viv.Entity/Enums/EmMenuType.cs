using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 菜单节点类型。决定这一行在客户端是什么角色，跟具体前端框架无关
    /// </summary>
    public enum EmMenuType : byte
    {
        /// <summary>
        /// 目录：只有子节点，自己没有页面
        /// </summary>
        [Description("目录")]
        Directory = 0,

        /// <summary>
        /// 菜单页：出现在菜单里，点了能跳
        /// </summary>
        [Description("页面")]
        Page = 1,

        /// <summary>
        /// 纯路由：不进菜单，只能被代码跳转（详情页、编辑页之类）
        /// </summary>
        [Description("路由")]
        Route = 2
    }
}
