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
        public LoggerType LoggerType { get; set; } = LoggerType.Serilog;
    }
}
