using System;

namespace Viv.Log
{
    /// <summary>
    /// 兜底日志器（无持久化，仅输出到控制台）
    /// </summary>
    /// <remarks>
    /// 1. 当未指定有效日志框架（LoggerType=None）时作为兜底方案；
    /// 2. 仅输出日志内容到控制台，无文件/数据库持久化；
    /// 3. 异常信息使用精简解析，避免控制台输出冗余。
    /// </remarks>
    public class NoneLogger : IVivLogger
    {
        private readonly string _logPrefix;

        /// <summary>
        /// 初始化兜底日志器
        /// </summary>
        public NoneLogger()
        {
            _logPrefix = VivLogFactory.CurrentOptions.LoggerName;
        }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            var logHeader = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {_logPrefix}";
            Console.WriteLine($"{logHeader} {message}");
            if (exception != null)
            {
                var exceptionMsg = ExceptionAnalyzer.ParseSimple(exception);
                Console.WriteLine($"{logHeader} 异常：{exceptionMsg}");
            }
        }
    }
}