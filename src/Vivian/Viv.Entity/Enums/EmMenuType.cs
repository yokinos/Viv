using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 菜单节点类型。只分「有没有自己的页面」，跟具体前端框架无关：
    /// 详情页、编辑页这类"代码跳转但不进菜单"的页面走 AtMenuSubPage + IsVisible=false
    /// </summary>
    public enum EmMenuType : byte
    {
        /// <summary>
        /// 目录：只有子节点，自己没有页面
        /// </summary>
        [Description("目录")]
        Directory = 0,

        /// <summary>
        /// 页面：自己能跳，是否显示在菜单里看 IsVisible
        /// </summary>
        [Description("页面")]
        Page = 1
    }
}
