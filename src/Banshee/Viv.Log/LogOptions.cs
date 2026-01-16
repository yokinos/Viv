using System;
using System.Collections.Generic;
using System.Text;

#nullable disable   
namespace Viv.Log
{
    public class LogOptions
    {
        private static LogOptions _options;

        /// <summary>
        /// 当前日志配置选项
        /// </summary>
        public static LogOptions CurrentOptions => _options ??= new LogOptions();

        public static void SetLogOptions(LogOptions options)
        {
            _options = options;
        }

        public LoggerType LoggerType { get; set; } = LoggerType.None;

        public string ConfigFilePath { get; set; } = string.Empty;

        public string LoggerName { get; set; } = "Viv.Logger";
    }
}
