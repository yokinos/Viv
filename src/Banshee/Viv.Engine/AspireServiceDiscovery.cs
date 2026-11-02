using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Viv.Echo.Grpc;

namespace Viv.Engine
{
    /// <summary>
    /// Aspire 服务发现：解析 services__* 环境变量（AppHost 通过 WithReference 注入），
    /// 得到每个被引用服务的名称、地址与网关短名。
    /// 仅当网关由 AppHost 启动时才有数据；独立启动时为空。
    /// </summary>
    public static class AspireServiceDiscovery
    {
        private const string ServicesEnvPrefix = "services__";

        public sealed record GatewayService(string Name, Uri Uri, string ShortName);

        /// <summary>
        /// 枚举 Aspire 注入的 services__* 环境变量。同一服务有多个端点（http/https）时优先 http。
        /// </summary>
        /// <param name="shortNames">服务名 → 短名；配了就用配置，没配才按服务名推断</param>
        public static List<GatewayService> Load(IReadOnlyDictionary<string, string>? shortNames = null)
        {
            var found = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var key = entry.Key?.ToString();
                var value = entry.Value?.ToString();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) ||
                    !key.StartsWith(ServicesEnvPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var remainder = key.Substring(ServicesEnvPrefix.Length);
                // gRPC 端点（services__viv-apex-api__grpc__0）和 REST 都是 http，
                // 环境变量顺序不定。并进同一条服务会把网关指到 HTTP/2 端口。
                if (IsGrpcEndpoint(remainder))
                {
                    continue;
                }

                var name = SplitServiceName(remainder);
                if (string.IsNullOrEmpty(name) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                if (!found.TryGetValue(name, out var existing) ||
                    (uri.Scheme == Uri.UriSchemeHttp && existing.Scheme != Uri.UriSchemeHttp))
                {
                    found[name] = uri;
                }
            }

            var services = found
                .Select(kv => new GatewayService(kv.Key, kv.Value, ResolveShortName(kv.Key, found.Keys, shortNames)))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            EnsureUniqueShortNames(services);
            return services;
        }

        /// <summary>
        /// 决定网关短名：配置优先（VivGateway:ShortNames），没配才推断。
        /// 推断规则：split('-') 取第二段（viv-apex-api -> apex）；第二段冲突时 -api 保留基础短名，
        /// 其余角色拼接剩余段（viv-herta-link -> hertalink）。
        /// 结果统一转小写并校验字符集 —— 短名会拼进路由模板，带 / 或空格会在运行期才炸。
        /// </summary>
        public static string ResolveShortName(
            string name,
            ICollection<string> allNames,
            IReadOnlyDictionary<string, string>? configured = null)
        {
            // 配了就用配置（配成空串也当场报错，不悄悄退回推断 —— 那会让人以为改了没生效）
            if (configured != null && configured.TryGetValue(name, out var configuredName))
                return Normalize(name, configuredName);

            return Normalize(name, InferShortName(name, allNames));
        }

        /// <summary>短名唯一性：两个服务撞一个名字，路由和集群会互相覆盖</summary>
        public static void EnsureUniqueShortNames(IReadOnlyList<GatewayService> services)
        {
            var duplicates = services
                .GroupBy(s => s.ShortName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count == 0) return;

            var detail = string.Join("；", duplicates.Select(g =>
                $"{g.Key} ← {string.Join(", ", g.Select(s => s.Name))}"));

            throw new InvalidOperationException(
                $"网关短名重复：{detail}。请在网关配置 VivGateway:ShortNames 里给其中一个换名字");
        }

        private static string InferShortName(string name, ICollection<string> allNames)
        {
            var parts = name.Split('-');
            if (parts.Length < 2)
            {
                return name;
            }

            var baseName = parts[1];

            var collides = allNames.Any(n =>
                n.Split('-').Length >= 2
                && n.Split('-')[1].Equals(baseName, StringComparison.OrdinalIgnoreCase)
                && !n.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (!collides)
            {
                return baseName;
            }

            // 冲突：-api 保留基础短名；其它角色（-link/-worker 等）拼接剩余段
            var rest = string.Concat(parts.Skip(2));
            return rest.Equals("api", StringComparison.OrdinalIgnoreCase) || rest.Length == 0 ? baseName : baseName + rest;
        }

        private static string Normalize(string serviceName, string shortName)
        {
            var value = (shortName ?? string.Empty).Trim().ToLowerInvariant();

            if (value.Length == 0 || !value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                throw new InvalidOperationException(
                    $"网关短名不合法：服务 {serviceName} 的短名 '{shortName}'，只允许小写字母、数字和连字符");
            }

            return value;
        }

        /// <summary>
        /// gRPC 端点名与 <see cref="VivGrpcExtensions.ServiceDiscoveryEndpointName"/> 一致。
        /// 服务名本身含不了这个段（按 <c>__</c> 切开后整段相等才算）。
        /// </summary>
        private static bool IsGrpcEndpoint(string remainder)
        {
            var parts = remainder.Split("__", StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => part.Equals(
                VivGrpcExtensions.ServiceDiscoveryEndpointName,
                StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 去掉 services__ 之后末尾的端点描述段，还原服务名：
        ///   viv-apex-api__http__0         -> viv-apex-api
        ///   viv-apex-api__0               -> viv-apex-api
        ///   viv-apex-api__default__0      -> viv-apex-api
        ///   viv-apex-api__0__endpoints__0 -> viv-apex-api
        /// grpc 段不在这里剥：剥掉会和 REST 合成同一个名字，http 地址就被 gRPC 端口盖掉。
        /// 那种 key 在 <see cref="Load"/> 里直接跳过。
        /// </summary>
        private static string? SplitServiceName(string remainder)
        {
            for (var i = 0; i < 3; i++)
            {
                var idx = remainder.LastIndexOf("__", StringComparison.Ordinal);
                if (idx <= 0)
                {
                    break;
                }

                var segment = remainder.Substring(idx + 2);
                if (segment.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("endpoints", StringComparison.OrdinalIgnoreCase) ||
                    segment.All(char.IsDigit))
                {
                    remainder = remainder.Substring(0, idx);
                }
                else
                {
                    break;
                }
            }

            return remainder;
        }
    }
}
