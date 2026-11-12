namespace Viv.Keke.Proxies
{
    /// <summary>
    /// 下游调用使用的 RPC 传输。由各服务的代理包（<c>Viv.Xxx.Proxy</c>）在注册时读取配置决定，
    /// 框架只提供两套客户端工厂，不在内部做“自动选择”。
    /// </summary>
    public enum VivRpcTransport
    {
        /// <summary>proto + 生成的 gRPC 客户端（现状）</summary>
        Grpc = 0,

        /// <summary>MagicOnion（C# 接口契约 + MessagePack）</summary>
        MagicOnion = 1,
    }
}
