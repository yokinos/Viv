using Serilog;
using System;

namespace Viv.Log
{
    /// <summary>
    /// Serilog 分布式日志
    /// </summary>
    public class SerilogDistributedLogger : IDistributedLogger
    {
        private readonly ILogger _logger;

        public SerilogDistributedLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        public void Error(string message, Exception ex, params object[] args)
        {
            _logger.Error(ex, message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void Fatal(string message, params object[] args)
        {
            _logger.Fatal(message, args);
        }

        public void Fatal(string message, Exception ex, params object[] args)
        {
            _logger.Fatal(message, ex, args);
        }

        public void Info(string message, params object[] args)
        {
            _logger.Information(message, args);
        }

        public void Warning(string message, params object[] args)
        {
            _logger.Warning(message, args);
        }
    }
}