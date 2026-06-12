using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion;
using Viv.Delusion.Extension;

namespace Viv.Log
{
    public class LogOptions
    {
        /// <summary>
        /// 日志框架类型  
        /// </summary>
        public LogType LogType { get; set; } = LogType.Serilog;

        /// <summary>
        /// 是否使用Seq
        /// </summary>
        public bool IsUseSeq { get; set; } = false;

        /// <summary>
        /// Seq服务地址
        /// </summary>
        public string SeqUrl { get; set; } = "http://localhost:5341";

        /// <summary>
        /// Seq API Key（可选，不配置则无需认证）
        /// </summary>
        public string SeqApiKey { get; set; } = string.Empty;
    }

    public class LoggerRegister
    {
        public static void Initialize(LogOptions options)
        {
            if (options.IsUseSeq && options.SeqUrl.IsNullOrEmpty())
            {
                throw new Exception("Seq地址不能为空");
            }

            VivConfigRegistry.Add(options);
        }
    }
}
