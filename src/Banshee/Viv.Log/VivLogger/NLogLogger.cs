using NLog;
using NLog.Config;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log.Enums;

namespace Viv.Log.VivLogger
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
            try
            {
                LogManager.Setup().LoadConfigurationFromFile(options.ConfigFilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"加载NLog配置文件失败：{options.ConfigFilePath}", ex);
            }
        }

        public void Log(VivLogLevel level, string message, Exception? exception = null)
        {
            var nlogLevel = level switch
            {
                VivLogLevel.Debug => NLog.LogLevel.Debug,
                VivLogLevel.Info => NLog.LogLevel.Info,
                VivLogLevel.Warn => NLog.LogLevel.Warn,
                VivLogLevel.Error => NLog.LogLevel.Error,
                VivLogLevel.Fatal => NLog.LogLevel.Fatal,
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
