var builder = DistributedApplication.CreateBuilder(args);


var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api")
    .WithDeveloperCertificateTrust(true) ;
var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api")
    .WithDeveloperCertificateTrust(true); ;
var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link")
    .WithDeveloperCertificateTrust(true); ;
var robinApi = builder.AddProject<Projects.Viv_Robin_Api>("viv-robin-api")
    .WithDeveloperCertificateTrust(true); ;

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
     .WithReference(apexApi)
     .WithReference(hertaApi)
     .WithReference(hertaLink)
     .WithReference(robinApi)
     .WithDeveloperCertificateTrust(true) ;

builder.Build().Run();
