using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    public class LogOptions
    {
        /// <summary>
        /// 日志框架类型  
        /// </summary>
        public LoggerType LoggerType { get; set; } = LoggerType.None;

        /// <summary>
        /// 日志的配置文件路径
        /// </summary>
        public string ConfigFilePath { get; set; } = string.Empty;

        /// <summary>
        /// 日志记录器的名称
        /// </summary>
        public string LoggerName { get; set; } = "[Viv.Log]";
    }
}
