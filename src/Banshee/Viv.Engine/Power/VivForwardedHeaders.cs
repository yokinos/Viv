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
    /// 按环境分档，不再无条件信任任意来源：
    /// - 配了 <see cref="EnvOptions.TrustedProxies"/>：只信任这些 IP / CIDR（生产推荐，容器网段如 172.16.0.0/12）。
    /// - 没配 + Development：清空 allow list，信任任意来源（本机与 Aspire 调试方便）。
    /// - 没配 + 其他环境：保留 ASP.NET 默认（仅 loopback）。宁可丢掉转发头，也不接受任意来源伪造。
    /// </summary>
    public static class VivForwardedHeaders
    {
        public static ForwardedHeadersOptions Create(EnvOptions? env, bool isDevelopment)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                   | ForwardedHeaders.XForwardedProto
                                   | ForwardedHeaders.XForwardedHost
            };

            var trusted = env?.TrustedProxies;
            if (trusted is { Length: > 0 })
            {
                // 先解析，确实拿到有效条目才清空默认值 —— 否则配错一个空串就等于把 loopback 也丢了。
                var proxies = new List<IPAddress>();
                var networks = new List<System.Net.IPNetwork>();
                foreach (var entry in trusted)
                {
                    Parse(entry, proxies, networks);
                }

                if (proxies.Count > 0 || networks.Count > 0)
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                    foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
                    foreach (var network in networks) options.KnownIPNetworks.Add(network);
                }

                return options;
            }

            if (isDevelopment)
            {
                // 开发环境：网关 IP 不固定（Aspire 动态端口 / 局域网机器名），直接信任转发头。
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }

            return options;
        }

        private static void Parse(string? entry, List<IPAddress> proxies, List<System.Net.IPNetwork> networks)
        {
            if (string.IsNullOrWhiteSpace(entry)) return;
            var text = entry.Trim();

            if (text.Contains('/') && System.Net.IPNetwork.TryParse(text, out var network))
            {
                networks.Add(network);
                return;
            }

            if (IPAddress.TryParse(text, out var proxy))
            {
                proxies.Add(proxy);
            }
        }
    }
}
