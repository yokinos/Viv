using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Viv.Engine
{
    /// <summary>
    /// API 与网关共用的 CORS 装配：生产只认显式 Origin，禁止 AllowAnyOrigin；
    /// 开发环境未配 Origin 时放行任意来源（回显具体 Origin，才和 AllowCredentials 共存），
    /// 覆盖局域网 IP / 机器名访问 dev server 的场景。
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
                // 回显请求的 Origin：浏览器用局域网 IP / 主机名打开 dev server 时，
                // 它并不是 loopback，只认 IsLoopbackOrigin 会被拦成跨域。
                policy.SetIsOriginAllowed(static _ => true);
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
