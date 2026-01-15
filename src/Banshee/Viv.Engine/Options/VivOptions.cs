using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cache.Redis;
using Viv.Engine.Enums;

namespace Viv.Engine.Options
{
    public class VivOptions
    {
        /// <summary>
        /// 缓存设置
        /// </summary>
        public CacheOptions? CacheOptions { get; set; }
    }
}
