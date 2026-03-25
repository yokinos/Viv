using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log.Enums;

namespace Viv.Log.VivLogger
{
    public class SerilogLogger : IVivLogger
    {
        private static bool isInit = false;
        private readonly ILogger _logger;

        public SerilogLogger()
        {
            if (!isInit)
            {
                Serilog.Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Debug()
                     .WriteTo.Console()
                     .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                     .CreateLogger();
            }


            _logger = Serilog.Log.Logger;
            isInit = true;
        }


        public void Log(VivLogLevel level, string message, Exception? exception = null)
        {
            switch (level)
            {
                case VivLogLevel.Debug:
                    _logger.Debug(message, exception);
                    break;
                case VivLogLevel.Info:
                    _logger.Information(message, exception);
                    break;
                case VivLogLevel.Warn:
                    _logger.Warning(message, exception);
                    break;
                case VivLogLevel.Error:
                    _logger.Error(message, exception);
                    break;
                case VivLogLevel.Fatal:
                    _logger.Fatal(message, exception);
                    break;
                default:
                    _logger.Information(message, exception);
                    break;
            }
        }
    }
}
