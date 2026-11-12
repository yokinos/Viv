namespace Viv.Keke.Identity
{
    /// <summary>
    /// 身份头验签失败（签名不匹配 / 时间戳过期 / 缺签名）。
    /// 抛出点不依赖任何传输类型，由适配层映射成各自的错误：gRPC → <c>RpcException(Unauthenticated)</c>，
    /// MagicOnion → <c>ReturnStatusException</c>/<c>RpcException</c>。
    /// </summary>
    public sealed class VivIdentityException : Exception
    {
        public VivIdentityException(string message) : base(message)
        {
        }
    }
}
