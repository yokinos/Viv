using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Sandrone.Impl;

namespace Viv.Engine
{
    public static class VivStartWorkerExtensions
    {
        /// <summary>
        /// 配置 Viv Worker 基础服务：加载配置、Autofac 容器、AddViv、编码注册
        /// 需要先调用 builder.AddServiceDefaults()
        /// </summary>
        public static HostApplicationBuilder AddVivWorker(this HostApplicationBuilder builder)
        {
            // 绑定 + 写 VivEngine.VivOptions 快照 + 注册进 DI，三件事在这一次调用里做完
            var vivOptions = builder.AddVivConfig();

            // Autofac 容器
            builder.ConfigureContainer(new AutofacServiceProviderFactory(), container =>
            {
                container.VivAutofacRegister(vivOptions);
            });

            // 基础服务
            builder.Services.AddViv(vivOptions);

            if (vivOptions.LogOption != null && vivOptions.LogOption.LogType == Log.LogType.Serilog)
            {
                // 不能 ClearProviders：它会把 AddServiceDefaults 注册的 OTel 日志 Provider 一起清掉，
                // 于是 Aspire 面板没有日志。改成只关 MS 自带控制台，全量放行给 Serilog。
                builder.Logging.AddSerilog(dispose: false);
                builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.None);
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return builder;
        }

        /// <summary>
        /// Build → VivLocator.Initialize → Run，阻塞到停止
        /// </summary>
        public static void RunVivWorker(this HostApplicationBuilder builder)
        {
            var host = builder.Build();
            VivLocator.Initialize(host.Services);

            host.Run();
        }
    }
}
