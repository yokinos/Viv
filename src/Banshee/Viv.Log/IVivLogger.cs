using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    /// <summary>
    /// 日志约束接口
    /// </summary>
    public interface IVivLogger
    {
        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志内容</param>
        /// <param name="exception">异常信息（可选）</param>
        void Log(LogLevel level, string message, Exception? exception = null);

        void Debug(string message) => Log(LogLevel.Debug, message);

        void Info(string message) => Log(LogLevel.Info, message);
        
        void Warn(string message) => Log(LogLevel.Warn, message);
        
        void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
        
        void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);
    }
}
