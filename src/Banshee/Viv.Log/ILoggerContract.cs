using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    /// <summary>
    /// 日志
    /// </summary>
    public interface ILoggerContract
    {
        void Info(string message, params object[] args);

        void Error(string message, Exception ex, params object[] args);

        void Error(string message, params object[] args);

        void Debug(string message, params object[] args);

        void Warning(string message, params object[] args);

        void Fatal(string message, params object[] args);

        void Fatal(string message, Exception ex, params object[] args);
    }
}
