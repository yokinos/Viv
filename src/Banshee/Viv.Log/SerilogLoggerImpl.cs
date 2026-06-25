using Serilog;
using System;
using Viv.Delusion;
using Viv.Delusion.Extension;

namespace Viv.Log
{
    /// <summary>
    /// Serilog
    /// </summary>
    public class SerilogLoggerImpl : ILoggerContract
    {
        private readonly ILogger _logger;

        public SerilogLoggerImpl()
        {
            _logger = Serilog.Log.Logger;
        }

        public void Debug(string message, params object[] args) => _logger.Debug(message, args);
        public void Info(string message, params object[] args) => _logger.Information(message, args);
        public void Warning(string message, params object[] args) => _logger.Warning(message, args);
        public void Error(string message, params object[] args) => _logger.Error(message, args);
        public void Error(string message, Exception ex, params object[] args) => _logger.Error(ex, message, args);
        public void Fatal(string message, params object[] args) => _logger.Fatal(message, args);
        public void Fatal(string message, Exception ex, params object[] args) => _logger.Fatal(ex, message, args);
    }
}