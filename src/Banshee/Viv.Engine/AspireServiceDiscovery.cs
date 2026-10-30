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
        public static List<GatewayService> Load()
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

            return found
                .Select(kv => new GatewayService(kv.Key, kv.Value, ToShortName(kv.Key, found.Keys)))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 短名推导：split('-') 取第二段（viv-apex-api -> apex）。
        /// 第二段冲突时（如 viv-herta-api 与 viv-herta-link 都是 herta），
        /// 默认 -api 服务保留基础短名，其余服务拼接剩余段（herta + link -> hertalink），保证集群 ID 唯一。
        /// </summary>
        private static string ToShortName(string name, ICollection<string> allNames)
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
