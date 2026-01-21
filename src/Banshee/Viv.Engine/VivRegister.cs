using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// 统一Viv注册入口
    /// </summary>
    internal class VivRegister
    {
        /// <summary>
        /// 注册入口
        /// </summary>
        /// <param name="vivOptions"></param>
        public static void Register(VivOptions vivOptions)
        {
            RegisterLog(vivOptions.LogOptions);
            if (vivOptions.CacheOptions.CacheProviderType == Enums.DistributedCacheType.Redis)
            {
                RegisterRedis(vivOptions.CacheOptions.RedisOptions);
            }
        }

        /// <summary>
        /// 注册日志
        /// </summary>
        /// <param name="options"></param>
        private static void RegisterLog(LogOptions options)
        {
            VivLogFactory.SetLogOptions(options);
        }

        /// <summary>
        /// 注册Redis
        /// </summary>
        /// <param name="options"></param>
        private static void RegisterRedis(RedisOptions options)
        {
            RedisFactory.Initialize(options);
        }
    }
}
