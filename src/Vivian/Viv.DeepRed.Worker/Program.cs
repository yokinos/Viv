using Viv.Engine;

namespace Viv.DeepRed.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivWorker();
        builder.Services.AddHostedService<Worker>();
        builder.RunVivWorker();
    }
}
