using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Serilog;
using System;
using Viv.Vva;

namespace Viv.Log
{
    /// <summary>
    /// Serilog 分布式日志（纯代码实现，无注入，无ILogger）
    /// </summary>
    public class SerilogDistributedLogger : IDistributedLogger
    {
        private readonly ILogger _logger;

        public SerilogDistributedLogger()
        {
            var options = VivConfigRegistry.Get<LogOptions>() ?? new LogOptions();

            var factory = new LoggerConfiguration()
                 .MinimumLevel.Debug()
                 .Enrich.FromLogContext()
                 .WriteTo.Console()
                 .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

            // 启用 ELK
            if (options.IsUseELK)
            {
                var elasticUris = new[] { new Uri(options.ELKUrl ?? "http://localhost:9200") };
                factory.WriteTo.Elasticsearch(elasticUris, opts =>
                {
                    opts.DataStream = new DataStreamName("logs", "viv-distributed", "production");
                    opts.BootstrapMethod = BootstrapMethod.Failure;
                },
                transport =>
                {
                    // 二选一：账号密码 或 ApiKey
                    if (!string.IsNullOrEmpty(options.ELKUserName) && !string.IsNullOrEmpty(options.ELKPassword))
                    {
                        transport.Authentication(new BasicAuthentication(options.ELKUserName, options.ELKPassword));
                    }
                    else if (!string.IsNullOrEmpty(options.ELKApiKey))
                    {
                        transport.Authentication(new ApiKey(options.ELKApiKey));
                    }
                });
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