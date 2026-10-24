using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 操作的功能模块
    /// </summary>
    public enum EmOperationModule
    {
        /// <summary>
        /// 菜单管理
        /// </summary>
        [Description("菜单管理")]
        Menu = 1,

        /// <summary>
        /// 用户管理
        /// </summary>
        [Description("用户管理")]
        User = 2,

        /// <summary>
        /// 角色管理
        /// </summary>
        [Description("角色管理")]
        Role = 3,

        /// <summary>
        /// 系统配置
        /// </summary>
        [Description("系统配置")]
        SystemConfig = 5
    }
}
