using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Elysia.Base
{
    /// <summary>
    /// 程序启动后会自动将所有的基础信息加载到这个类中
    /// </summary>
    public class AppMemoryCache
    {
        public static string GetAppSecret(long appId)
        {
            return "AppSecret";
        }
    }
}
