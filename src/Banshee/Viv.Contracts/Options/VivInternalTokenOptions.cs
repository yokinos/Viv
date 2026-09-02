namespace Viv.Contracts.Options
{
    /// <summary>
    /// 内部请求 HMAC 密钥（x-request-token）。由 LoadVivConfig 写入 VivConfigRegistry，
    /// Echo gRPC 拦截器与 HTTP RequestTokenResolver 共用，不回落到 JWT SecretKey。
    /// </summary>
    public sealed class VivInternalTokenOptions
    {
        public string? InternalToken { get; set; }

        public string? ServiceName { get; set; }
    }
}
