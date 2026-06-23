using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using Viv.Aoi;

namespace Viv.Engine
{
    public static class VivWorkerExtensions
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
