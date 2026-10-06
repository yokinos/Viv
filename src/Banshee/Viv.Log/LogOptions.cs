using Serilog;
using Serilog.Events;
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
        /// 全局最低日志级别，默认 Information（排查时按服务调到 Debug）
        /// </summary>
        public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

        /// <summary>
        /// 是否输出到控制台
        /// </summary>
        public bool IsUseConsole { get; set; } = true;

        /// <summary>
        /// 是否输出到文件
        /// </summary>
        public bool IsUseFile { get; set; } = true;

        /// <summary>
        /// 文件日志路径（Serilog File sink 语法，按天滚动）
        /// </summary>
        public string LogFilePath { get; set; } = "logs/log-.txt";

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
}
