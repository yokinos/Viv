using Serilog;
using Viv.Delusion;

namespace Viv.Log
{
    public static class SerilogProvider
    {
        private static bool _initialized;
        private static readonly Lock _lock = new();

        public static void Initialize(LogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    var config = new LoggerConfiguration()
                        .MinimumLevel.Is(options.MinimumLevel)
                        .Enrich.FromLogContext();

                    if (options.IsUseConsole) config.WriteTo.Console();
                    if (options.IsUseFile) config.WriteTo.File(options.LogFilePath, rollingInterval: RollingInterval.Day);

                    if (options.IsUseSeq)
                    {
                        config.WriteTo.Seq(
                            serverUrl: options.SeqUrl ?? "http://localhost:5341",
                            apiKey: string.IsNullOrEmpty(options.SeqApiKey) ? null : options.SeqApiKey);
                    }

                    Serilog.Log.Logger = config.CreateLogger();
                }
                catch (Exception ex)
                {
                    Serilog.Debugging.SelfLog.WriteLine($"日志初始化失败: {ex}");
                    Serilog.Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Is(options.MinimumLevel)
                        .WriteTo.Console()
                        .CreateLogger();
                }

                _initialized = true;
            }
        }

        public static ILogger GetLogger() => Serilog.Log.Logger;

        /// <summary>
        /// Serilog 全局 logger 是否已经建起来。
        ///
        /// 给装配期摘要用：没初始化时 <see cref="Serilog.Log.Logger"/> 是 SilentLogger，
        /// 写进去等于丢掉，调用方据此决定要不要退回控制台。
        /// </summary>
        public static bool IsInitialized => _initialized;

        public static void CloseAndFlush()
        {
            if (_initialized)
                Serilog.Log.CloseAndFlush();
        }
    }
}
