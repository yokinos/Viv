namespace Viv.Echo
{
    public class EchoOptions
    {
        public bool EnableHttp { get; set; } = true;

        public GrpcOptions? GrpcOption { get; set; }
    }

    public class GrpcOptions
    {
        /// <summary>
        /// gRPC 服务端开关（AddVivApi 读取，配置驱动装配 Kestrel + 自动发现映射）
        /// </summary>
        public bool EnableServer { get; set; }

        /// <summary>
        /// gRPC 服务端专用端口（严格 HTTP/2）
        /// </summary>
        public int Port { get; set; }
    }
}
