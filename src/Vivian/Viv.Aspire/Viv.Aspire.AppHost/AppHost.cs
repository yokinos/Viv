var builder = DistributedApplication.CreateBuilder(args);

var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api")
    .WithDeveloperCertificateTrust(true);

var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api")
    .WithDeveloperCertificateTrust(true);

var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link")
    .WithDeveloperCertificateTrust(true);

var deepRedApi = builder.AddProject<Projects.Viv_DeepRed_Api>("viv-deepred-api")
    .WithDeveloperCertificateTrust(true);

var sakumaiApi = builder.AddProject<Projects.Viv_SakuMai_Api>("viv-sakumai-api")
    .WithDeveloperCertificateTrust(true);

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
    .WithReference(apexApi)
    .WithReference(hertaApi)
    .WithReference(hertaLink)
    .WithReference(deepRedApi)
    .WithReference(sakumaiApi)
    .WithDeveloperCertificateTrust(true);

builder.Build().Run();
