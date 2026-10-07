using Viv.Aspire.ServiceDefaults;
using Viv.Engine;

namespace Viv.Ouroboros.Worker;

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