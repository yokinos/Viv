using Viv.Engine;

namespace Viv.Aspire.Gateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 启用 Aspire 服务发现
        builder.AddServiceDefaults();
        // 仅开发环境信任自签名证书；生产默认校验，防止 MITM
        builder.AddVivGateway(ignoreSslErrors: builder.Environment.IsDevelopment());
        builder.RunVivGateway(app => app.MapDefaultEndpoints());
    }
}
