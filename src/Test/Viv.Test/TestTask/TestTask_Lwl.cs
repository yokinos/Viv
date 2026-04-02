using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Viv.Aoi;
using Viv.Authentication.Enums;
using Viv.Engine.Enums;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo.Interface;
using Viv.Redis;
using Viv.Test.Core;

namespace Viv.Test.TestTask
{
    [CommandSet(Command.LwL)]
    public class TestTask_Lwl : ITestTask
    {
        public async Task StartAsync()
        {
            // 🔥 Viv 框架完整配置
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
                    LogType = LogType.Serilog
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
                        ConnectionString = "localhost:6379,password=vivRedis",
                        SentinelEndPoints = [],
                        SentinelMasterName = "MasterRedisNode",
                        AbortOnConnectFail = true,
                        AllowAdmin = true,
                        ConnectTimeout = 5000,
                        DefaultDatabase = 0,
                        KeepAlive = 60,
                        MaxDbIndex = 12,
                        SyncTimeout = 5000,
                        Password = "vivRedis"
                    }
                },

                // 消息队列（RabbitMQ + Redis 发布订阅）
                NanaOption = new Nana.Options.NanaOptions
                {
                    MainQueueType = Nana.Enums.MessageQueueType.RabbitMQ,
                    SecondaryQueueType = Nana.Enums.MessageQueueType.RedisPubSub,
                    ConsumerTypes = [], // 不开启消费者
                    IsEnableLocalMessage = false,
                    RabbitMqOptions = new Nana.Options.RabbitMqOptions
                    {
                        HostName = "localhost",
                        UserName = "viv",
                        Password = "vivRabbitMQ",
                        Port = 5672,
                        VirtualHost = "/Viv"
                    },
                    RetryCount = 3
                },

                // 数据库（读写分离 + 自动实体扫描）
                DatabaseOption = new Momo.Options.DatabaseOptions
                {
                    DatabaseSouce = Momo.Enums.DatabaseSouceType.SqlServer,
                    MasterConnectionString = "Server=localhost;Database=vivApex;User Id=sa;Password=<PASSWORD>!;",
                    SlaveConnectionStrings = ["Server=localhost;Database=vivApexRead;User Id=sa;Password=<PASSWORD>!;"],
                    IsReadWriteSplit = true,
                    Timeout = 30,
                    EntityTyoeOptions =
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
                    SecretKey = "1x24as5da56d4qd1w65qd1",
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