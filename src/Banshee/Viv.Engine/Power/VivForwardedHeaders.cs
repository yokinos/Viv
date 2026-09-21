using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Viv.Engine.Options;

namespace Viv.Engine.Power
{
    /// <summary>
    /// Aspire / YARP 网关通常不在 loopback。ASP.NET 默认 KnownProxies/KnownIPNetworks 只有 127.0.0.1/::1，
    /// 容器网络里的网关 IP 不被信任，X-Forwarded-* 会被丢掉，下游再 UseHttpsRedirection 就会 302 出网关。
    ///
    /// 默认清空已知代理限制（信任反向代理传入的转发头）。
    /// 生产要收紧时在 <see cref="EnvOptions.TrustedProxies"/> 填 IP / CIDR。
    /// </summary>
    public static class VivForwardedHeaders
    {
        public static ForwardedHeadersOptions Create(EnvOptions? env)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                   | ForwardedHeaders.XForwardedProto
                                   | ForwardedHeaders.XForwardedHost
            };

            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            var trusted = env?.TrustedProxies;
            if (trusted is not { Length: > 0 })
            {
                return options;
            }

            foreach (var entry in trusted)
            {
                Add(options, entry);
            }

            return options;
        }

        private static void Add(ForwardedHeadersOptions options, string? entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return;
            var text = entry.Trim();

            if (text.Contains('/') && System.Net.IPNetwork.TryParse(text, out var network))
            {
                options.KnownIPNetworks.Add(network);
                return;
            }

            if (IPAddress.TryParse(text, out var proxy))
            {
                options.KnownProxies.Add(proxy);
            }
        }
    }
}
