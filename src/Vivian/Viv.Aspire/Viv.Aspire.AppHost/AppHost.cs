var builder = DistributedApplication.CreateBuilder(args);


var apexApi = builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api");
var hertaApi = builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api");
var hertaLink = builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link");
var robinApi = builder.AddProject<Projects.Viv_Robin_Api>("viv-robin-api");

builder.AddProject<Projects.Viv_Aspire_Gateway>("viv-aspire-gateway")
     .WithReference(apexApi)
     .WithReference(hertaApi)
     .WithReference(hertaLink)
     .WithReference(robinApi);

builder.Build().Run();
