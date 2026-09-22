using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Options;

namespace Viv.Momo.Interface
{
    /// <summary>
    /// 允许动态获取数据库访问配置项的接口
    /// </summary>
    public interface IDatabaseOptionsProvider
    {
        /// <summary>
        /// 获取数据库访问配置项
        /// </summary>
        /// <returns></returns>
        DatabaseOptions GetRealOptions();
    }
}
