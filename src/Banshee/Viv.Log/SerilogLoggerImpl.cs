using Serilog;
using System;
using Viv.Vva;
using Viv.Vva.Extension;

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
            var options = VivConfigRegistry.Get<LogOptions>() ?? new LogOptions();

            var factory = new LoggerConfiguration()
                 .MinimumLevel.Debug()
                 .Enrich.FromLogContext()
                 .WriteTo.Console()
                 .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

            // 启用 Seq
            if (options.IsUseSeq)
            {
                factory.WriteTo.Seq(
                    serverUrl: options.SeqUrl ?? "http://localhost:5341",
                    apiKey: options.SeqApiKey.IsNullOrEmpty() ? null : options.SeqApiKey
                );
            }

            _logger = factory.CreateLogger();
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