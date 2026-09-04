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
        /// <param name="defaultOptions">当前AppSettings中的数据库配置</param>
        /// <returns></returns>
        DatabaseOptions GetOptions(DatabaseOptions defaultOptions);
    }
}
