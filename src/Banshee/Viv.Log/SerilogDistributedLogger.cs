using Serilog;
using System;

namespace Viv.Log
{
    /// <summary>
    /// Serilog 分布式日志（现代、高性能、结构化）
    /// </summary>
    public class SerilogDistributedLogger : IDistributedLogger
    {
        private readonly ILogger _logger;

        public SerilogDistributedLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Info(string message, params object[] args)
            => _logger.Information(message, args);

        public void Error(Exception ex, string message, params object[] args)
            => _logger.Error(ex, message, args);

        public void Debug(string message, params object[] args)
            => _logger.Debug(message, args);

        public void Warn(string message, params object[] args)
            => _logger.Warning(message, args);

        public void Error(Exception ex, params object[] args)
            => _logger.Error(ex, string.Empty, args);
    }
}