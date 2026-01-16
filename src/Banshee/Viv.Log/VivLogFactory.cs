using System;
using System.Collections.Generic;
using System.Text;

#nullable disable   
namespace Viv.Log
{
    /// <summary>
    /// 对外提供日志器实例及初始化配置的的工厂类
    /// </summary>
    public sealed class VivLogFactory
    {
        private static LogOptions _options;

        /// <summary>
        /// 当前日志配置选项
        /// </summary>
        public static LogOptions CurrentOptions => _options ??= new LogOptions();

        /// <summary>
        /// 懒加载日志器实例
        /// </summary>
        private static readonly ResettableLazy<IVivLogger> _lazyVivLogger = new(() =>
        {
            return CurrentOptions.LoggerType switch
            {
                LoggerType.None => new NoneLogger(),
                LoggerType.Log4net => new Log4netLogger(),
                LoggerType.NLog => new NLogLogger(),
                _ => new NoneLogger()
            };
        });

        /// <summary>
        /// 获取日志器实例
        /// </summary>
        /// <returns></returns>
        public static IVivLogger GetLogger()
        {
            return _lazyVivLogger.Value;
        }

        /// <summary>
        /// 用于设置日志配置选项
        /// </summary>
        /// <param name="options"></param>
        public static void SetLogOptions(LogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(LogOptions));
            _options = options;

            if (_lazyVivLogger.IsValueCreated)
            {
                _lazyVivLogger.Reset();
            }
        }
    }
}
