namespace Viv.Contracts.Options
{
    /// <summary>
    /// 内部请求 HMAC 密钥（x-request-token）。由 <c>VivConfigLoader</c> 从 <c>EnvOptions</c> 派生并注册进 DI，
    /// Echo gRPC 拦截器构造注入读取（HTTP RequestTokenResolver 取的是同一份值，但走 <c>VivEngine.VivOptions.EnvOption</c>），
    /// 不回落到 JWT SecretKey。
    /// </summary>
    public sealed class VivInternalTokenOptions
    {
        public string? InternalToken { get; set; }

        public string? ServiceName { get; set; }
    }
}
