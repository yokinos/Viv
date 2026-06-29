using Serilog;
using Viv.Delusion;

namespace Viv.Log
{
    public static class SerilogProvider
    {
        private static bool _initialized;
        private static readonly Lock _lock = new();

        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    var options = VivConfigRegistry.Get<LogOptions>() ?? new LogOptions();

                    var config = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .Enrich.FromLogContext()
                        .WriteTo.Console()
                        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

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
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        .CreateLogger();
                }

                _initialized = true;
            }
        }

        public static ILogger GetLogger() => Serilog.Log.Logger;

        public static void CloseAndFlush()
        {
            if (_initialized)
                Serilog.Log.CloseAndFlush();
        }
    }
}
