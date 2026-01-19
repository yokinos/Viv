using log4net;
using log4net.Config;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log.Enums;

namespace Viv.Log.VivLogger
{
    public class Log4netLogger : IVivLogger
    {
        private readonly ILog _logger;

        public Log4netLogger()
        {
            var options = VivLogFactory.CurrentOptions;
            LoadLog4netConfig(options);
            _logger = LogManager.GetLogger(options.LoggerName);
        }

        private static void LoadLog4netConfig(LogOptions options)
        {
            try
            {
                var fileInfo = new FileInfo(options.ConfigFilePath);
                if (!fileInfo.Exists)
                    throw new FileNotFoundException("Log4net配置文件不存在", options.ConfigFilePath);

                XmlConfigurator.Configure(fileInfo);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"加载Log4net配置文件失败：{options.ConfigFilePath}", ex);
            }
        }

        public void Log(VivLogLevel level, string message, Exception? exception = null)
        {
            switch (level)
            {
                case VivLogLevel.Debug:
                    _logger.Debug(message, exception);
                    break;
                case VivLogLevel.Info:
                    _logger.Info(message, exception);
                    break;
                case VivLogLevel.Warn:
                    _logger.Warn(message, exception);
                    break;
                case VivLogLevel.Error:
                    _logger.Error(message, exception);
                    break;
                case VivLogLevel.Fatal:
                    _logger.Fatal(message, exception);
                    break;
                default:
                    _logger.Info(message, exception);
                    break;
            }
        }

        public ValueTask LogAsync(VivLogLevel level, string message, Exception? exception = null)
        {
            Log(level, message, exception);
            return ValueTask.CompletedTask;
        }
    }
}
