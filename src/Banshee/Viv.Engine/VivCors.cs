using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Viv.Engine
{
    /// <summary>
    /// API 与网关共用的 CORS 装配：显式 Origin，禁止 AllowAnyOrigin。
    /// </summary>
    public static class VivCors
    {
        public static void Apply(CorsPolicyBuilder policy, string[]? origins, bool isDevelopment, bool allowCredentials)
        {
            policy.AllowAnyHeader().AllowAnyMethod();

            var configured = origins?.Where(static o => !string.IsNullOrWhiteSpace(o)).ToArray() ?? [];
            if (configured.Length > 0)
            {
                policy.WithOrigins(configured);
                if (configured.Any(static o => o.Contains('*', StringComparison.Ordinal)))
                {
                    policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                }
            }
            else if (isDevelopment)
            {
                policy.SetIsOriginAllowed(IsLoopbackOrigin);
            }

            if (allowCredentials)
            {
                policy.AllowCredentials();
            }
        }

        public static bool IsLoopbackOrigin(string origin)
        {
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
        }
    }
}
