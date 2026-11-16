using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Viv.Log;

namespace Viv.Engine
{
    /// <summary>
    /// Viv 的日志装配。
    ///
    /// 只收一件事：把 Serilog 接到宿主的 <see cref="ILoggingBuilder"/> 上。四个 starter
    /// （Api / Worker / Gateway / AppBuilder）各自保留独立的引导流程，但「怎么挂」只写一份 ——
    /// 同一段代码抄四遍时，连注释里那条坑都会长得各不相同。
    /// </summary>
    public static class LoggingBuilderExtensions
    {
        /// <summary>
        /// 配置了 Serilog 就接上；没配（<c>LogOption</c> 为 null）或选了别的实现就什么都不做。
        ///
        /// <b>不要改成 ClearProviders</b>：它会把 AddServiceDefaults 注册的 OpenTelemetry 日志
        /// Provider 一起清掉，Aspire 面板里就没有日志了。这里只把 MS 自带的控制台关掉，
        /// 让位给 Serilog 自己的 Console sink，其余 Provider 全量放行。
        /// </summary>
        public static ILoggingBuilder AttachVivLogging(this ILoggingBuilder logging, LogOptions? logOptions)
        {
            ArgumentNullException.ThrowIfNull(logging);

            if (logOptions is not { IsSerilog: true }) return logging;

            logging.AddSerilog(dispose: false);
            logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.None);
            return logging;
        }
    }
}
