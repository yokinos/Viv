using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log.Enums;

namespace Viv.Log.VivLogger
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
        void Log(VivLogLevel level, string message, Exception? exception = null);

        void Debug(string message) => Log(VivLogLevel.Debug, message);

        void Info(string message) => Log(VivLogLevel.Info, message);

        void Warn(string message) => Log(VivLogLevel.Warn, message);

        void Error(string message, Exception? exception = null) => Log(VivLogLevel.Error, message, exception);

        void Fatal(string message, Exception? exception = null) => Log(VivLogLevel.Fatal, message, exception);

        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志内容</param>
        /// <param name="exception">异常信息（可选）</param>
        ValueTask LogAsync(VivLogLevel level, string message, Exception? exception = null);

        ValueTask DebugAsync(string message) => LogAsync(VivLogLevel.Debug, message);

        ValueTask InfoAsync(string message) => LogAsync(VivLogLevel.Info, message);

        ValueTask WarnAsync(string message) => LogAsync(VivLogLevel.Warn, message);

        ValueTask ErrorAsync(string message, Exception? exception = null) => LogAsync(VivLogLevel.Error, message, exception);

        ValueTask FatalAsync(string message, Exception? exception = null) => LogAsync(VivLogLevel.Fatal, message, exception);
    }
}
