using Viv.Cli;
using Viv.Engine;

namespace Viv.Toolbox
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = VivAppBuilder.Create(args);

            var vivHost = new VivCliHost(new CliOptions()
            {
                AppName = "Viv 业务侧通用工具箱",
                BannerTitle = "Viv.Toolbox",
            }, builder.Services);

            builder.AddVivApp();

            var provider = await builder.StartAsync();
            vivHost.UseContainer(provider);

            try
            {
                await vivHost.RunAsync();
            }
            finally
            {
                await builder.StopAsync();
            }
        }
    }
}
