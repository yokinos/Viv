using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cache.Redis;
using Viv.Engine.Enums;

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
        /// API响应结果设置
        /// </summary>
        public VivApiResultOptions ApiResultOptions { get; set; }
    }
}
