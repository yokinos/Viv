namespace Viv.Echo
{
    public class EchoOptions
    {
        public bool EnableHttp { get; set; } = true;

        /// <summary>
        /// gRPC / MagicOnion 宿主配置。节点存在即表示这个进程要占一条服务端口（两种传输共用同一条
        /// 严格 HTTP/2 明文端口，只是服务路径不同）。
        /// </summary>
        public GrpcOptions? GrpcOption { get; set; }
    }

    /// <summary>
    /// 服务端协议。迁移期用 <see cref="Both"/> 双宿主：proto 路径与 MagicOnion 路径同时可用，
    /// 客户端按边切换时不会因为“服务端只开了其中一边”而整条链路失败。
    /// </summary>
    [Flags]
    public enum EchoProtocol
    {
        None = 0,

        Grpc = 1,

        MagicOnion = 2,

        Both = Grpc | MagicOnion,
    }

    public class GrpcOptions
    {
        /// <summary>
        /// 旧开关：等价于 <c>Protocol = Grpc</c>。保留是为了让已入库的 appsettings.json 不做迁移。
        /// </summary>
        public bool EnableServer { get; set; }

        /// <summary>
        /// 服务端专用端口（严格 HTTP/2）。两种传输共用。
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 服务端协议。不配（<see cref="EchoProtocol.None"/>）时回落到 <see cref="EnableServer"/> 的旧语义。
        /// </summary>
        public EchoProtocol Protocol { get; set; } = EchoProtocol.None;

        /// <summary>实际生效的协议：<see cref="Protocol"/> 优先，没配则看 <see cref="EnableServer"/>。</summary>
        public EchoProtocol EffectiveProtocol => Protocol != EchoProtocol.None
            ? Protocol
            : EnableServer ? EchoProtocol.Grpc : EchoProtocol.None;

        /// <summary>是否需要注册 gRPC 服务端（拦截器 + 自动发现 + MapGrpcService）。</summary>
        public bool ServesGrpc => (EffectiveProtocol & EchoProtocol.Grpc) != 0;

        /// <summary>是否需要注册 MagicOnion 服务端（由 <c>Viv.Echo.MagicOnion</c> 的扩展完成）。</summary>
        public bool ServesMagicOnion => (EffectiveProtocol & EchoProtocol.MagicOnion) != 0;
    }
}
