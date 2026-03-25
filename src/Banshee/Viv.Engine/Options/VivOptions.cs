using System;
using System.Collections.Generic;
using System.Text;
using Viv.Aoi;
using Viv.Engine.Enums;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Redis;

#nullable disable
namespace Viv.Engine.Options
{
    public class VivOptions
    {
        public VivOptions() { }

        /// <summary>
        /// 环境
        /// </summary>
        public VivEnv Env { get; set; } = VivEnv.Development;

        /// <summary>
        /// 当前程序的DI设置
        /// </summary>
        public DIOptions DIOptions { get; set; }

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
