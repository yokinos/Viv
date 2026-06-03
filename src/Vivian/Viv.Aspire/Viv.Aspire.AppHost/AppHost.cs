using Viv.Aspire.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var infra = RegisterCenter.Register(builder);

var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api")
    .WithEnvironment("ConnectionStrings__Redis", infra.RedisConnection)
    .WithEnvironment("RabbitMq__Host", infra.RabbitMqHost)
    .WithEnvironment("RabbitMq__Port", infra.RabbitMqPort)
    .WithEnvironment("RabbitMq__UserName", infra.RabbitMqUser)
    .WithEnvironment("RabbitMq__Password", infra.RabbitMqPassword)
    .WithEnvironment("RabbitMq__VirtualHost", infra.RabbitMqVirtualHost)
    .WithReference(infra.RedisResource)
    .WithReference(infra.RabbitMqResource)
    .WithDeveloperCertificateTrust(true);

var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api")
    .WithEnvironment("ConnectionStrings__Redis", infra.RedisConnection)
    .WithEnvironment("RabbitMq__Host", infra.RabbitMqHost)
    .WithEnvironment("RabbitMq__Port", infra.RabbitMqPort)
    .WithEnvironment("RabbitMq__UserName", infra.RabbitMqUser)
    .WithEnvironment("RabbitMq__Password", infra.RabbitMqPassword)
    .WithEnvironment("RabbitMq__VirtualHost", infra.RabbitMqVirtualHost)
    .WithReference(infra.RedisResource)
    .WithReference(infra.RabbitMqResource)
    .WithDeveloperCertificateTrust(true);

var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link")
    .WithEnvironment("ConnectionStrings__Redis", infra.RedisConnection)
    .WithEnvironment("RabbitMq__Host", infra.RabbitMqHost)
    .WithEnvironment("RabbitMq__Port", infra.RabbitMqPort)
    .WithEnvironment("RabbitMq__UserName", infra.RabbitMqUser)
    .WithEnvironment("RabbitMq__Password", infra.RabbitMqPassword)
    .WithEnvironment("RabbitMq__VirtualHost", infra.RabbitMqVirtualHost)
    .WithReference(infra.RedisResource)
    .WithReference(infra.RabbitMqResource)
    .WithDeveloperCertificateTrust(true);

var deepRedApi = builder.AddProject<Projects.Viv_DeepRed_Api>("viv-deepred-api")
    .WithEnvironment("ConnectionStrings__Redis", infra.RedisConnection)
    .WithEnvironment("RabbitMq__Host", infra.RabbitMqHost)
    .WithEnvironment("RabbitMq__Port", infra.RabbitMqPort)
    .WithEnvironment("RabbitMq__UserName", infra.RabbitMqUser)
    .WithEnvironment("RabbitMq__Password", infra.RabbitMqPassword)
    .WithEnvironment("RabbitMq__VirtualHost", infra.RabbitMqVirtualHost)
    .WithReference(infra.RedisResource)
    .WithReference(infra.RabbitMqResource)
    .WithDeveloperCertificateTrust(true);

var sakumaiApi = builder.AddProject<Projects.Viv_SakuMai_Api>("viv-sakumai-api")
    .WithEnvironment("ConnectionStrings__Redis", infra.RedisConnection)
    .WithEnvironment("RabbitMq__Host", infra.RabbitMqHost)
    .WithEnvironment("RabbitMq__Port", infra.RabbitMqPort)
    .WithEnvironment("RabbitMq__UserName", infra.RabbitMqUser)
    .WithEnvironment("RabbitMq__Password", infra.RabbitMqPassword)
    .WithEnvironment("RabbitMq__VirtualHost", infra.RabbitMqVirtualHost)
    .WithReference(infra.RedisResource)
    .WithReference(infra.RabbitMqResource)
    .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
    .WithReference(apexApi)
    .WithReference(hertaApi)
    .WithReference(hertaLink)
    .WithReference(deepRedApi)
    .WithReference(sakumaiApi)
    .WithDeveloperCertificateTrust(true);

builder.Build().Run();
