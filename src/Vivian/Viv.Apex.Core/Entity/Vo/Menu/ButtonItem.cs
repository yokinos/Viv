using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Vo.Menu
{
    /// <summary>
    /// 菜单按钮（权限点），随登录数据一起下发
    /// </summary>
    public class ButtonItem
    {
        /// <summary>
        /// 按钮Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 所属菜单Id
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 按钮名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 按钮权限位
        /// </summary>
        public int BitIndex { get; set; }
    }
}
