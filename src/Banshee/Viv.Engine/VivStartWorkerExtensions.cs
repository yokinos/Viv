using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Engine.Impl;

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
            var vivOptions = VivEngine.LoadVivConfig();
            ArgumentNullException.ThrowIfNull(vivOptions);

            // Autofac 容器
            builder.ConfigureContainer(new AutofacServiceProviderFactory(), container =>
            {
                container.VivAutofacRegister(vivOptions.DIOption);
            });

            // 基础服务
            builder.Services.AddViv(vivOptions);
            builder.Services.AddScoped<IAiClientFactory, AiClientFactory>();

            if (vivOptions.LogOption != null && vivOptions.LogOption.LogType == Log.LogType.Serilog)
            {
                // Serilog 替换宿主 ILogger
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog(dispose: false);
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
