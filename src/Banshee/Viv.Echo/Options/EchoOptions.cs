namespace Viv.Echo.Options
{
    public class EchoOptions
    {
        public bool EnableHttp { get; set; } = true;

        public List<GrpcServiceEndpoint> GrpcEndpoints { get; set; } = [];
    }

    /// <summary>
    /// gRPC 服务端点（旧方式，推荐使用 [GrpcClient] 特性标注接口）
    /// </summary>
    [Obsolete("推荐使用 [GrpcClient(\"name\", \"address\")] 特性标注接口，编译时自动生成注册代码")]
    public class GrpcServiceEndpoint
    {
        public string Name { get; set; } = default!;

        public string Address { get; set; } = default!;

        public string ClientTypeAssembly { get; set; } = default!;

        public string ClientTypeFullName { get; set; } = default!;
    }
}
