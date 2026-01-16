using NLog;
using NLog.Config;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    public class NLogLogger : IVivLogger
    {
        private readonly Logger _logger;

        /// <summary>
        /// 初始化NLog日志器
        /// </summary>
        public NLogLogger()
        {
            var options = VivLogFactory.CurrentOptions;
            LoadNLogConfig(options);
            _logger = LogManager.GetLogger(options.LoggerName);
        }

        /// <summary>
        /// 加载NLog配置
        /// </summary>
        private static void LoadNLogConfig(LogOptions options)
        {
            if (string.IsNullOrEmpty(options.ConfigFilePath))
            {
                LogManager.Setup().LoadConfigurationFromFile();
                return;
            }

            try
            {
                LogManager.Setup().LoadConfigurationFromFile(options.ConfigFilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"加载NLog配置文件失败：{options.ConfigFilePath}", ex);
            }
        }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            var nlogLevel = level switch
            {
                LogLevel.Debug => NLog.LogLevel.Debug,
                LogLevel.Info => NLog.LogLevel.Info,
                LogLevel.Warn => NLog.LogLevel.Warn,
                LogLevel.Error => NLog.LogLevel.Error,
                LogLevel.Fatal => NLog.LogLevel.Fatal,
                _ => NLog.LogLevel.Info
            };

            if (exception == null)
                _logger.Log(nlogLevel, message);
            else
            {
                var exceptionMsg = ExceptionAnalyzer.Parse(exception);
                _logger.Log(nlogLevel, $"{message}\n{exceptionMsg}");
            }
        }
    }
}
