using Viv.Engine;

namespace Viv.Aspire.Gateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 启用 Aspire 服务发现
        builder.AddServiceDefaults();
        builder.AddVivGateway();
        builder.RunVivGateway(app => app.MapDefaultEndpoints());
    }
}
