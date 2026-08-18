using System.Text;
using Microsoft.Extensions.Configuration;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// VivEngine.LoadVivConfig(IConfiguration) 从 appsettings.json 的 VivOptions 节点绑定。
/// 验证 MS ConfigurationBinder 语义：枚举("0")/数值/bool/数组/BaseType 字符串/null 节点跳过，
/// JSON 形状对齐真实服务配置（含 EntityTypeOptions.BaseType 程序集限定名）。
/// 与 RequestTokenResolverTests 共享禁用并行集合（都改静态 VivEngine.VivOptions）。
/// </summary>
[Collection("VivEngineStaticState")]
public class VivConfigBindingTests
{
    private const string Json = /* lang=json */
        """
        {
          "VivOptions": {
            "EnvOption": { "InternalToken": "tok-abc", "Env": 0, "ServiceName": "viv.apex.api", "MachineId": 101 },
            "DIOption": null,
            "CacheOption": { "CacheProviderType": 1, "RedisOptions": { "RedisMode": 0, "ConnectionString": "x", "SentinelEndPoints": [], "MaxDbIndex": 12 }, "IsEnableMemoryCache": true },
            "LogOption": { "LogType": 1, "IsUseSeq": true, "SeqUrl": "https://seq", "SeqApiKey": "k" },
            "DatabaseOption": {
              "DatabaseSource": 0,
              "MasterConnectionString": "server=x;database=viv_test",
              "SlaveConnectionStrings": [],
              "Timeout": 30,
              "EntityTypeOptions": [
                {
                  "AssemblyName": "Viv.Entity",
                  "Namespace": "Viv.Entity.Database.Apex",
                  "BaseType": "Viv.Momo.Interface.IEntity, Viv.Momo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                  "ClassNameEndsWith": "",
                  "ClassNameStartsWith": ""
                }
              ]
            },
            "NanaOption": { "Host": "h", "Port": 5672, "UserName": "u", "Password": "p", "VirtualHost": "v", "RetryCount": 3, "ConsumerTypes": [], "SagaDatabaseSource": 0, "SagaConnectionString": "s" },
            "TokenOption": null,
            "TickOption": null,
            "EchoOption": { "EnableHttp": true, "GrpcOption": null },
            "S3Option": null
          }
        }
        """;

    [Fact]
    public void 从VivOptions节点绑定VivOptions()
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Json)))
            .Build();

        var options = VivEngine.LoadVivConfig(config);

        // EnvOption
        Assert.Equal("tok-abc", options.EnvOption!.InternalToken);
        Assert.Equal("viv.apex.api", options.EnvOption.ServiceName);
        Assert.Equal(101, options.EnvOption.MachineId);
        Assert.Equal(0, (int)options.EnvOption.Env);

        // JSON null 节点应保持 null（不实例化默认对象）
        Assert.Null(options.DIOption);
        Assert.Null(options.TokenOption);
        Assert.Null(options.TickOption);
        Assert.Null(options.S3Option);

        // CacheOption（枚举 + 嵌套 + bool + 空数组）
        Assert.Equal(1, (int)options.CacheOption!.CacheProviderType);
        Assert.Equal("x", options.CacheOption.RedisOptions!.ConnectionString);
        Assert.Empty(options.CacheOption.RedisOptions.SentinelEndPoints);
        Assert.Equal(12, options.CacheOption.RedisOptions.MaxDbIndex);
        Assert.True(options.CacheOption.IsEnableMemoryCache);

        // DatabaseOption（数组 + BaseType 字符串绑定，扫描时由 TypeScanMagic 解析）
        Assert.Equal(0, (int)options.DatabaseOption!.DatabaseSource);
        Assert.Equal("server=x;database=viv_test", options.DatabaseOption.MasterConnectionString);
        Assert.Empty(options.DatabaseOption.SlaveConnectionStrings);
        var entity = Assert.Single(options.DatabaseOption.EntityTypeOptions);
        Assert.Equal("Viv.Entity", entity.AssemblyName);
        Assert.Equal("Viv.Entity.Database.Apex", entity.Namespace);
        Assert.Equal("Viv.Momo.Interface.IEntity, Viv.Momo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", entity.BaseType);
        Assert.Equal(typeof(Viv.Momo.Interface.IEntity), Type.GetType(entity.BaseType!));

        // NanaOption（数值/数组）
        Assert.Equal(5672, options.NanaOption!.Port);
        Assert.Equal("u", options.NanaOption.UserName);
        Assert.Empty(options.NanaOption.ConsumerTypes);

        // EchoOption（null 子节点）
        Assert.True(options.EchoOption!.EnableHttp);
        Assert.Null(options.EchoOption.GrpcOption);
    }
}
