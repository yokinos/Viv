using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Shared
{
    /// <summary>
    /// 内部使用的日志记录器,WebApi需使用注入方式的是的实现[IVivLogger]接口
    /// </summary>
    public class VivLogger
    {
        public static void Error(Exception exception)
        {

        }

        public static void Error(string memssage)
        {

        }
    }
}
