using Viv.Elysia.Extension;
using Viv.Elysia.Filter;
using Viv.Engine;

namespace Viv.Apex.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(new ApiInitSetting("Viv Apex API", "apex"), mvc => mvc.Filters.AddElysiaFilter());
        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
