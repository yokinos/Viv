using Newtonsoft.Json;
using Viv.Aoi;
using Viv.Authentication.Enums;
using Viv.Engine.Enums;
using Viv.Engine.Options;
using Viv.Momo.Interface;
using Viv.Redis;
using Viv.Test.Core;
using Viv.Log;

namespace Viv.Test.TestTask
{
    [CommandSet(Command.LwL)]
    public class TestTask_Lwl : ITestTask
    {
        public async Task StartAsync()
        {
            // Viv 框架完整配置
            var options = new VivOptions
            {
                // 环境
                Env = VivEnv.Development,

                // 自动DI配置（Service/Repository 自动扫描注册）
                DIOption = new DIOptions
                {
                    // 服务自动注册
                    ServiceImplementation = new Vva.Magic.FilterTypeOptions
                    {
                        AssemblyName = "Viv.Apex.Core",
                        NameSpace = "Viv.Apex.Core.Service",
                        BaseType = null,
                        ClassNameEndWith = "Service"
                    },

                    // 仓储自动注册
                    RepositoryImplementation = new Vva.Magic.FilterTypeOptions
                    {
                        AssemblyName = "Viv.Apex.Core",
                        NameSpace = "Viv.Apex.Core.Repository",
                        BaseType = null,
                        ClassNameEndWith = "Repository"
                    }
                },

                // 日志
                LogOption = new Log.LogOptions
                {
                    LogType = LogType.Serilog,
                    IsUseSeq = true,
                    SeqApiKey = string.Empty,
                    SeqUrl = "https://es.katoumegumi.net",
                },

                // 缓存（二级缓存：内存 + Redis）
                CacheOption = new VivCacheOptions
                {
                    CacheProviderType = DistributedCacheType.Redis,
                    IsEnableMemoryCache = true,
                    RedisOptions = new Redis.RedisOptions
                    {
                        RedisMode = RedisMode.Standalone,
                        SelectorType = DbSelectorType.None,
                        ConnectionString = "43.228.79.205:6379,password=viv_redis_77",
                        SentinelEndPoints = [],
                        SentinelMasterName = "MasterRedisNode",
                        AbortOnConnectFail = true,
                        AllowAdmin = true,
                        ConnectTimeout = 5000,
                        DefaultDatabase = 0,
                        KeepAlive = 60,
                        MaxDbIndex = 12,
                        SyncTimeout = 5000,
                        Password = "viv_redis_77"
                    }
                },

                // 消息队列（RabbitMQ via MassTransit）
                NanaOption = new Nana.Options.NanaOptions
                {
                    Host = "43.228.79.205",
                    Port = 5672,
                    UserName = "guest",
                    Password = "viv_rabbitmq_77",
                    VirtualHost = "/Viv",
                    RetryCount = 3,
                    ConsumerTypes = [] // 不开启消费者
                },

                // 数据库（读写分离 + 自动实体扫描）
                DatabaseOption = new Momo.Options.DatabaseOptions
                {
                    DatabaseSouce = Momo.Enums.DatabaseSouceType.PostgreSQL,
                    MasterConnectionString = "Server=43.228.79.205;Database=viv_apex;User Id=sa;Password=viv_pgsql_77;",
                    SlaveConnectionStrings = [],
                    IsReadWriteSplit = false,
                    Timeout = 30,
                    EntityTypeOptions =
                    [
                        new Vva.Magic.FilterTypeOptions
                        {
                            AssemblyName = "Viv.Entity.Apex",
                            NameSpace = "Viv.Entity.Apex.Database",
                            BaseType = typeof(IEntity)
                        }
                    ]
                },
                TokenOption = new Authentication.TokenOptions()
                {
                    TokenType = TokenType.Jwt,
                    SecretKey = "VivsK2pR5xQ8dGjN3mL6tHfBvYwApex",
                    Audience = string.Empty,
                    ExpireMinutes = 120,
                    Issuer = string.Empty
                }
            };

            // 格式化输出完整配置 JSON
            Out.PrintlnFormatJson(options);
            await Task.CompletedTask;
        }
    }
}
