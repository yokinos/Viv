using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Vue
{
    public class ButtonItem
    {
        /// <summary>
        /// 菜单Id
        /// </summary>
        public long MenuId { get; set; }

        /// <summary>
        /// 按钮名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 按钮掩码
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 按钮Id
        /// </summary>
        public long Id { get; set; }
    }
}
