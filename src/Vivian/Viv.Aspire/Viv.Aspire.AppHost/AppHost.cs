var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Viv_Aspire_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Viv_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api");

builder.AddProject<Projects.Viv_Herta_Api>("viv-herta-api");

builder.AddProject<Projects.Viv_Herta_Link>("viv-herta-link");

builder.AddProject<Projects.Viv_Robin_Api>("viv-robin-api");

builder.Build().Run();
