using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine.Enums;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Redis;

#nullable disable
namespace Viv.Engine.Options
{
    public record VivOptions
    {
        /// <summary>
        /// 缓存设置
        /// </summary>
        public VivCacheOptions CacheOptions { get; set; }

        /// <summary>
        /// 日志设置
        /// </summary>
        public LogOptions LogOptions { get; set; }

        /// <summary>
        /// 数据库设置
        /// </summary>
        public DatabaseOptions DatabaseOptions { get; set; }

        /// <summary>
        /// MQ设置
        /// </summary>
        public NanaOptions NanaOptions { get; set; }
    }
}
