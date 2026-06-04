using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Viv.Aspire.Gateway.Magic;
using Viv.Aspire.Gateway.Options;

namespace Viv.Aspire.Gateway;
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 加载 YARP 配置
        builder.Configuration.AddJsonFile("viv.yarp.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile("viv.ratelimit.json", optional: false, reloadOnChange: true);

        // 启用 Aspire 服务发现
        builder.AddServiceDefaults();
        builder.Services.AddAllHttpClientsIgnoreSslErrors();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = "Viv.Aspire.Gateway";
                options.RequireHttpsMetadata = false;
            });

        builder.Services.AddAuthorization();

        // 注册 YARP
        builder.Services.AddReverseProxy()
             .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        var env = builder.Environment;

        /* https 证书自动续期
        if (env.IsProduction())
        {
            builder.Services.AddLettuceEncrypt()
                .PersistDataToDirectory(new DirectoryInfo(Path.Combine(env.ContentRootPath, "certs")), "");
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.ListenAnyIP(80);
                kestrel.ListenAnyIP(443,
                    portOptions => { portOptions.UseHttps(h => { h.UseLettuceEncrypt(kestrel.ApplicationServices); }); });
            });
        }
        */

        // 限流配置
        builder.Services.Configure<VivRateLimitOptions>(builder.Configuration.GetSection(VivRateLimitOptions.CustomRateLimit));
        var rateLimitOptions = new VivRateLimitOptions();
        builder.Configuration.GetSection(VivRateLimitOptions.CustomRateLimit).Bind(rateLimitOptions);

        #region CORS
        string defaultCorsPolicyName = "DefaultCors";
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(defaultCorsPolicyName, policy =>
            {
                policy.SetIsOriginAllowedToAllowWildcardSubdomains().AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            });
        });
        #endregion

        #region Cache
        string defaultCachePolicyName = "DefaultCache";
        builder.Services.AddOutputCache(options =>
        {
            string expire = "20";
            int expireSeconds = int.Parse(expire);
            options.AddPolicy("NoCache", build => build.NoCache());
            options.AddPolicy(defaultCachePolicyName, build => build.Expire(TimeSpan.FromSeconds(20)));
            options.AddPolicy("CustomCache", build => build.Expire(TimeSpan.FromSeconds(expireSeconds)));
        });
        #endregion

        #region Rate Limiter
        string defaultRateLimiterPolicyName = "DefaultRateLimiter";
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(defaultRateLimiterPolicyName, opt =>
            {
                opt.PermitLimit = 4;
                opt.Window = TimeSpan.FromSeconds(12);
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                opt.QueueLimit = 2;
            });
            options.AddFixedWindowLimiter("CustomRateLimiter", opt =>
            {
                opt.PermitLimit = rateLimitOptions.PermitLimit;
                opt.Window = TimeSpan.FromSeconds(rateLimitOptions.Window);
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                opt.QueueLimit = rateLimitOptions.QueueLimit;
            });
        });
        #endregion

        var app = builder.Build();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });

        app.UseCors(defaultCorsPolicyName);
        app.UseOutputCache();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
                var userName = context.User.FindFirstValue(ClaimTypes.Name) ?? "";

                context.Request.Headers["X-User-Id"] = userId;
                context.Request.Headers["X-User-Name"] = userName;
            }

            await next();
        });

        // YARP
        app.MapReverseProxy(proxyPipeline =>
        {
            proxyPipeline.UseSessionAffinity();
            proxyPipeline.UseLoadBalancing();
            proxyPipeline.UsePassiveHealthChecks();
        });

        app.MapDefaultEndpoints();
        app.Run();
    }
}