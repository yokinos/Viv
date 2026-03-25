var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Viv_Aspire_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Viv_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.Viv_Chat_Api>("viv-chat-api");

builder.AddProject<Projects.Viv_Chat_Line>("viv-chat-line");

builder.AddProject<Projects.Viv_King_Api>("viv-king-api");

builder.AddProject<Projects.Viv_Apex_Api>("viv-apex-api");

builder.Build().Run();
