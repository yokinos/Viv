using System;

namespace Viv.Log
{
    /// <summary>
    /// 静态日志工具类（内部使用，提供极简的静态方法日志记录）
    /// </summary>
    /// <remarks>
    /// 1. 懒加载初始化日志器，首次调用时自动匹配LogOptions配置；
    /// 2. 无需依赖注入，直接通过静态方法调用（Info/Error等）；
    /// 3. 未配置日志框架时自动使用NoneLogger（控制台输出）兜底；
    /// 4. 在WebAPI/ASP.NET Core等依赖注入场景中，推荐使用构造函数注入IVivLogger实例，以支持：
    ///    - 更灵活的日志器生命周期管理；
    ///    - 不同模块使用不同日志配置；
    ///    - 单元测试时轻松模拟日志器。
    /// </remarks>
    public class WriteLogger
    {
        private static IVivLogger Logger => VivLogFactory.GetLogger();

        /// <summary>
        /// 记录Info级别日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Info(string message)
        {
            Logger.Log(LogLevel.Info, message);
        }

        /// <summary>
        /// 记录Error级别日志（仅异常）
        /// </summary>
        /// <param name="exception">异常信息</param>
        public static void Error(Exception exception)
        {
            Logger.Log(LogLevel.Error, string.Empty, exception);
        }

        /// <summary>
        /// 记录Error级别日志（仅消息）
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Error(string message)
        {
            Logger.Log(LogLevel.Error, message);
        }

        /// <summary>
        /// 记录Error级别日志（消息+异常，补充核心重载）
        /// </summary>
        /// <param name="message">日志内容</param>
        /// <param name="exception">异常信息</param>
        public static void Error(string message, Exception exception)
        {
            Logger.Log(LogLevel.Error, message, exception);
        }


        /// <summary>
        /// 记录Debug级别日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Debug(string message)
        {
            Logger.Log(LogLevel.Debug, message);
        }

        /// <summary>
        /// 记录Warn级别日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Warn(string message)
        {
            Logger.Log(LogLevel.Warn, message);
        }

        /// <summary>
        /// 记录Fatal级别日志
        /// </summary>
        /// <param name="message">日志内容</param>
        /// <param name="exception">异常信息（可选）</param>
        public static void Fatal(string message, Exception? exception = null)
        {
            Logger.Log(LogLevel.Fatal, message, exception);
        }
    }
}