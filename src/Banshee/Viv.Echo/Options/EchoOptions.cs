namespace Viv.Echo.Options
{
    public class EchoOptions
    {
        public bool EnableHttp { get; set; } = true;

        public List<GrpcServiceEndpoint> GrpcEndpoints { get; set; } = [];
    }

    public class GrpcServiceEndpoint
    {
        public string Name { get; set; } = default!;

        public string Address { get; set; } = default!;

        public string ClientTypeAssembly { get; set; } = default!;

        public string ClientTypeFullName { get; set; } = default!;
    }
}
