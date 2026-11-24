using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Extension;
using Viv.Engine;

namespace Viv.Ouroboros.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi();
        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
