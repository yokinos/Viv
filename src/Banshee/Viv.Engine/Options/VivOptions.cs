using System;
using System.Collections.Generic;
using System.Text;
using Viv.Aoi;
using Viv.Clockwork.Options;
using Viv.Contracts.Enums;
using Viv.Contracts.Options;
using Viv.Echo.Options;
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
        public TickOptions TickOption { get; set; }

        /// <summary>
        /// 跨服务通信配置（HTTP + gRPC）
        /// </summary>
        public EchoOptions EchoOption { get; set; }

        /// <summary>
        /// 默认的OpenAI配置（用于调用OpenAI API）
        /// </summary>
        public OpenAIOptions OpenAIOption { get; set; }

        /// <summary>
        /// S3 配置（用于存储和检索文件）
        /// </summary>
        public S3Options S3Option { get; set; }
    }
}
