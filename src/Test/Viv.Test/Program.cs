using Viv.Cli;

namespace Viv.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var host = new VivCliHost(new CliOptions
            {
                BannerTitle = "Viv Test",
                AppName = "viv"
            });
            await host.RunAsync();
        }
    }
}
