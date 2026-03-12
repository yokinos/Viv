using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viv.Test.Core
{
    public enum Command
    {
        [CommandDescription("Clear", "清除当前所有输入输出", true)]
        Clear,

        [CommandDescription("Help", "获取所有命令", true)]
        Help,

        [CommandDescription("Exit", "退出程序", true)]
        Exit,

        [CommandDescription("Lwl", "lwl的测试程序")]
        LwL,
    }
}
