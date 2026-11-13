using System.Collections;
using System.Linq;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 网关从 services__* 选 REST 地址。gRPC 端点也是 http，不能跟 REST 抢同一条 URI。
/// </summary>
public class AspireServiceDiscoveryTests
{
    private static readonly object Gate = new();

    [Fact]
    public void Load_grpc端点不进入网关地址_http优先于https()
    {
        lock (Gate)
        {
            var snapshot = SnapshotServiceEnv();
            try
            {
                ClearServiceEnv();
                Environment.SetEnvironmentVariable("services__viv-apex-api__grpc__0", "http://127.0.0.1:7001");
                Environment.SetEnvironmentVariable("services__viv-apex-api__https__0", "https://127.0.0.1:7443");
                Environment.SetEnvironmentVariable("services__viv-apex-api__http__0", "http://127.0.0.1:5088");
                Environment.SetEnvironmentVariable("services__viv-herta-api__grpc__0", "http://127.0.0.1:7002");

                var found = AspireServiceDiscovery.Load();

                var apex = Assert.Single(found);
                Assert.Equal("viv-apex-api", apex.Name);
                var uri = Assert.Single(apex.Uris);
                Assert.Equal("http", uri.Scheme);
                Assert.Equal(5088, uri.Port);
                Assert.Equal("apex", apex.ShortName);
            }
            finally
            {
                ClearServiceEnv();
                Restore(snapshot);
            }
        }
    }

    [Fact]
    public void Load_只有https和grpc时用https()
    {
        lock (Gate)
        {
            var snapshot = SnapshotServiceEnv();
            try
            {
                ClearServiceEnv();
                Environment.SetEnvironmentVariable("services__viv-apex-api__GRPC__0", "http://127.0.0.1:7001");
                Environment.SetEnvironmentVariable("services__viv-apex-api__https__0", "https://127.0.0.1:7443");

                var found = AspireServiceDiscovery.Load();

                var apex = Assert.Single(found);
                var uri = Assert.Single(apex.Uris);
                Assert.Equal("https", uri.Scheme);
                Assert.Equal(7443, uri.Port);
            }
            finally
            {
                ClearServiceEnv();
                Restore(snapshot);
            }
        }
    }

    [Fact]
    public void Load_同一服务的多个副本都保留_并按序号排序()
    {
        lock (Gate)
        {
            var snapshot = SnapshotServiceEnv();
            try
            {
                ClearServiceEnv();
                // 写反顺序：结果不能依赖环境变量的枚举顺序
                Environment.SetEnvironmentVariable("services__viv-apex-api__http__1", "http://127.0.0.1:5089");
                Environment.SetEnvironmentVariable("services__viv-apex-api__http__0", "http://127.0.0.1:5088");

                var found = AspireServiceDiscovery.Load();

                var apex = Assert.Single(found);
                Assert.Equal(new[] { 5088, 5089 }, apex.Uris.Select(u => u.Port));
            }
            finally
            {
                ClearServiceEnv();
                Restore(snapshot);
            }
        }
    }

    [Fact]
    public void Load_同一序号上的http与https只留一个()
    {
        lock (Gate)
        {
            var snapshot = SnapshotServiceEnv();
            try
            {
                ClearServiceEnv();
                Environment.SetEnvironmentVariable("services__viv-apex-api__https__0", "https://127.0.0.1:7443");
                Environment.SetEnvironmentVariable("services__viv-apex-api__http__0", "http://127.0.0.1:5088");

                var found = AspireServiceDiscovery.Load();

                // 同一副本的两个端点混进 cluster 会有一半请求打到 TLS 端口上
                var uri = Assert.Single(Assert.Single(found).Uris);
                Assert.Equal(5088, uri.Port);
            }
            finally
            {
                ClearServiceEnv();
                Restore(snapshot);
            }
        }
    }

    private static Dictionary<string, string?> SnapshotServiceEnv()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key != null && key.StartsWith("services__", StringComparison.OrdinalIgnoreCase))
            {
                snapshot[key] = entry.Value?.ToString();
            }
        }

        return snapshot;
    }

    private static void ClearServiceEnv()
    {
        foreach (var key in SnapshotServiceEnv().Keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static void Restore(Dictionary<string, string?> snapshot)
    {
        foreach (var pair in snapshot)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
