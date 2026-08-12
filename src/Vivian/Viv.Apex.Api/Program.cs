using Viv.Elysia.Filter;
using Viv.Engine;

namespace Viv.Apex.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi("Viv Apex API", mvc => mvc.Filters.Add<RequestFilterAttribute>());
        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
