var builder = DistributedApplication.CreateBuilder(args);

// 敏感参数 — 开发环境有默认值，生产环境通过 Aspire CLI 或 secrets 注入
var dbPassword    = builder.AddParameter("VivDbPassword",    secret: true);
var rmqPassword   = builder.AddParameter("VivRmqPassword",   secret: true);
var redisPassword = builder.AddParameter("VivRedisPassword", secret: true);
var jwtSecret     = builder.AddParameter("VivJwtSecret",     secret: true);

var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api")
    .WithEnvironment("Viv__DatabaseOption__MasterConnectionString",
        $"Server=localhost;Database=vivApex;User Id=sa;Password={dbPassword};")
    .WithEnvironment("Viv__DatabaseOption__SlaveConnectionStrings__0",
        $"Server=localhost;Database=vivApexRead;User Id=sa;Password={dbPassword};")
    .WithEnvironment("Viv__NanaOption__SagaConnectionString",
        $"Server=localhost;Database=vivSaga;User Id=sa;Password={dbPassword};")
    .WithEnvironment("Viv__CacheOption__RedisOptions__ConnectionString",
        $"localhost:6379,password={redisPassword}")
    .WithEnvironment("Viv__CacheOption__RedisOptions__Password", redisPassword)
    .WithEnvironment("Viv__NanaOption__Host", "localhost")
    .WithEnvironment("Viv__NanaOption__Password", rmqPassword)
    .WithEnvironment("Viv__TokenOption__SecretKey", jwtSecret)
    .WithDeveloperCertificateTrust(true);

var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api")
    .WithReference(apexApi)
    .WithEnvironment("Viv__NanaOption__Host", "localhost")
    .WithEnvironment("Viv__NanaOption__Password", rmqPassword)
    .WithDeveloperCertificateTrust(true);

var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link")
    .WithEnvironment("Viv__NanaOption__Host", "localhost")
    .WithEnvironment("Viv__NanaOption__Password", rmqPassword)
    .WithDeveloperCertificateTrust(true);

var robinApi = builder.AddProject<Projects.Viv_Robin_Api>("viv-robin-api")
    .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
     .WithReference(apexApi)
     .WithReference(hertaApi)
     .WithReference(hertaLink)
     .WithReference(robinApi)
     .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Beatrice_Api>("viv-beatrice-api");

builder.AddProject<Projects.Viv_DeepRed_Api>("viv-deepred-api");

builder.AddProject<Projects.Viv_SakuMai_Api>("viv-sakumai-api");

builder.Build().Run();
