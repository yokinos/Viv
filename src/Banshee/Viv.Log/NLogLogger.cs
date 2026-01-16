using NLog.Config;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    public class NLogLogger : IVivLogger
    {
        private readonly NLog.Logger _logger;

        /// <summary>
        /// 初始化NLog日志器（自动读取全局配置）
        /// </summary>
        /// <param name="loggerName">日志器名称（默认：Viv.Logger）</param>
        public NLogLogger()
        {
            var options = LogOptions.CurrentOptions;

            // 加载全局配置文件（优先使用自定义路径，无则用默认配置）
            LoadNLogConfig(options);

            // 初始化NLog日志器
            _logger = LogManager.GetLogger(options.LoggerName);
        }

        /// <summary>
        /// 加载NLog配置（读取LogOptions中的配置文件路径）
        /// </summary>
        private void LoadNLogConfig(LogOptions options)
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
            // 映射统一日志级别到NLog原生级别
            var nlogLevel = level switch
            {
                LogLevel.Debug => global::NLog.LogLevel.Debug,
                LogLevel.Info => global::NLog.LogLevel.Info,
                LogLevel.Warn => global::NLog.LogLevel.Warn,
                LogLevel.Error => global::NLog.LogLevel.Error,
                LogLevel.Fatal => global::NLog.LogLevel.Fatal,
                _ => global::NLog.LogLevel.Info
            };

            // 记录日志（支持异常透传）
            if (exception == null)
                _logger.Log(nlogLevel, message);
            else
                _logger.Log(nlogLevel, exception, message);
        }
    }
}
