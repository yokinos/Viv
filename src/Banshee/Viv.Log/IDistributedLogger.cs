using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    /// <summary>
    /// 分布式日志
    /// </summary>
    public interface IDistributedLogger
    {
        void Info(string message, params object[] args);
        void Error(Exception ex, string message, params object[] args);
        void Error(Exception ex, params object[] args);
        void Debug(string message, params object[] args);
        void Warn(string message, params object[] args);
    }
}
