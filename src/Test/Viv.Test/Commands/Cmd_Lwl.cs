using Spectre.Console.Cli;
using Viv.Aoi;
using Viv.Authentication.Enums;
using Viv.Cli;
using Viv.Contracts.Enums;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Redis;

namespace Viv.Test.Commands
{
    [VivCommand("lwl", "输出 Viv 完整配置 JSON")]
    public class Cmd_Lwl : AsyncCommand
    {
        protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var options = new VivOptions
            {
                Env = VivEnv.Development,

                DIOption = new DIOptions
                {
                    ServiceImplementation = new Delusion.Magic.FilterTypeOptions
                    {
                        AssemblyName = "Viv.Apex.Core",
                        Namespace = "Viv.Apex.Core.Service",
                        ClassNameEndsWith = "Service"
                    },
                    RepositoryImplementation = new Delusion.Magic.FilterTypeOptions
                    {
                        AssemblyName = "Viv.Apex.Core",
                        Namespace = "Viv.Apex.Core.Repository",
                        ClassNameEndsWith = "Repository"
                    }
                },

                LogOption = new LogOptions
                {
                    LogType = LogType.Serilog,
                    IsUseSeq = true,
                    SeqApiKey = "WpoE1USw5Or3ZtiUuzOr",
                    SeqUrl = "https://seq.katoumegumi.net",
                },

                CacheOption = new VivCacheOptions
                {
                    CacheProviderType = DistributedCacheType.Redis,
                    IsEnableMemoryCache = true,
                    RedisOptions = new RedisOptions
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

                NanaOption = new Nana.Options.NanaOptions
                {
                    Host = "43.228.79.205",
                    SagaDatabaseSource = DatabaseSourceType.SqlServer,
                    SagaConnectionString = "server=43.228.79.205;user id=sa;password=viv_sqlserver_77;database=viv_saga_core;min pool size=4;max pool size=512;TrustServerCertificate=true;",
                    Port = 5672,
                    UserName = "Viv",
                    Password = "viv_rabbitmq_77",
                    VirtualHost = "/Viv",
                    RetryCount = 3,
                    ConsumerTypes = []
                },

                DatabaseOption = new Momo.Options.DatabaseOptions
                {
                    DatabaseSource = DatabaseSourceType.SqlServer,
                    MasterConnectionString = "server=43.228.79.205;user id=sa;password=viv_sqlserver_77;database=viv_apex_master;min pool size=4;max pool size=512;TrustServerCertificate=true;",
                    SlaveConnectionStrings = [],
                    IsReadWriteSplit = false,
                    Timeout = 30,
                    EntityTypeOptions =
                    [
                        new Delusion.Magic.FilterTypeOptions
                        {
                            AssemblyName = "Viv.Entity",
                            Namespace = "Viv.Entity.Database.Apex",
                            BaseType = typeof(IEntity)
                        }
                    ]
                },

                TokenOption = new Authentication.TokenOptions
                {
                    TokenType = TokenType.Jwt,
                    SecretKey = "VivsK2pR5xQ8dGjN3mL6tHfBvYwApex",
                    Audience = string.Empty,
                    ExpireMinutes = 120,
                    Issuer = string.Empty
                },

                EchoOption = new() { EnableGrpc = true, EnableHttp = true },

                TickOption = new()
                {
                    SchedulerType = Tick.Enums.VivSchedulerType.TickerQ,
                    TickerQ = new Tick.Options.TickerQOptions
                    {
                        ConnectionString = "server=43.228.79.205;user id=sa;password=viv_sqlserver_77;database=viv_tickerq_core;min pool size=4;max pool size=512;TrustServerCertificate=true;",
                        DashboardOptions = new Tick.Options.TickerQDashboradOptions(),
                        DatabaseSource = DatabaseSourceType.SqlServer,
                        EnableDashboard = true,
                    }
                }
            };

            Out.PrintlnFormatJson(options);
            return Task.FromResult(0);
        }
    }
}
