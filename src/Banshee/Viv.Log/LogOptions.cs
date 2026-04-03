using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Log
{
    public class LogOptions
    {
        /// <summary>
        /// 日志框架类型  
        /// </summary>
        public LogType LogType { get; set; } = LogType.Serilog;

        /// <summary>
        /// 是否使用ELK
        /// </summary>
        public bool IsUseELK { get; set; } = false;

        /// <summary>
        /// ELK地址
        /// </summary>
        public string ELKUrl { get; set; } = "http://localhost:9200";

        public string ELKApiKey { get; set; } = string.Empty;
        public string ELKUserName { get; private set; } = "elastic";
        public string ELKPassword { get; set; } = "viv_dev_elk_77";
    }

    public class LoggerRegister
    {
        public static void Initialize(LogOptions options)
        {
            if (options.IsUseELK && options.ELKUrl.IsNullOrEmpty())
            {
                throw new Exception("ELK地址不能为空");
            }

            VivConfigRegistry.Add(options);
        }
    }
}
