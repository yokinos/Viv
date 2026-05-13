using System;
using System.Collections.Generic;
using System.Text;
using Viv.Aoi;
using Viv.Authentication;
using Viv.Echo.Options;
using Viv.Engine.Enums;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Redis;
using Viv.Sayu.Options;

#nullable disable
namespace Viv.Engine.Options
{
    public class VivOptions
    {
        public VivOptions() { }

        /// <summary>
        /// 环境
        /// </summary>
        public VivEnv Env { get; set; }

        /// <summary>
        /// 当前程序的DI设置
        /// </summary>
        public DIOptions DIOption { get; set; }

        /// <summary>
        /// 缓存设置
        /// </summary>
        public VivCacheOptions CacheOption { get; set; }

        /// <summary>
        /// 日志设置
        /// </summary>
        public LogOptions LogOption { get; set; }

        /// <summary>
        /// 数据库设置
        /// </summary>
        public DatabaseOptions DatabaseOption { get; set; }

        /// <summary>
        /// MQ设置
        /// </summary>
        public NanaOptions NanaOption { get; set; }

        /// <summary>
        /// 令牌设置
        /// </summary>
        public TokenOptions TokenOption { get; set; }

        /// <summary>
        /// 定时任务配置
        /// </summary>
        public SayuOptions SayuOption { get; set; }

        /// <summary>
        /// 跨服务通信配置（HTTP + gRPC）
        /// </summary>
        public EchoOptions EchoOption { get; set; }
    }
}
