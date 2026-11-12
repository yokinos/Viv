using Viv.Contracts.Interface;

namespace Viv.Keke.Identity
{
    /// <summary>
    /// 一次调用要携带的 x-viv-* 身份头值快照（含签名 token）。
    ///
    /// 传输无关：gRPC 客户端拦截器（<c>VivGrpcInterceptor</c>）与 MagicOnion 客户端 Filter
    /// 都从这里取值，避免两条链路各拼一套头、各算一次签名 —— 签名载荷一旦漂移，
    /// 服务端验签会静默拒绝，而且两边单测各自都还是绿的。
    /// </summary>
    public sealed record VivIdentityValues(
        string AppId,
        string SubjectId,
        string UserId,
        string ServiceName,
        string HolderId,
        string? RequestToken = null)
    {
        /// <summary>
        /// 转成头键值对。键统一小写（gRPC metadata 写盘要求小写），没签出 token 时不带这一条。
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> ToHeaderPairs()
        {
            yield return Pair(VivHeaderContract.AppId, AppId);
            yield return Pair(VivHeaderContract.SubjectId, SubjectId);
            yield return Pair(VivHeaderContract.UserId, UserId);
            yield return Pair(VivHeaderContract.ServiceName, ServiceName);
            yield return Pair(VivHeaderContract.HolderId, HolderId);

            if (!string.IsNullOrWhiteSpace(RequestToken))
            {
                yield return Pair(VivHeaderContract.InnerRequestToken, RequestToken);
            }
        }

        private static KeyValuePair<string, string> Pair(string header, string value)
            => new(VivIdentityHeaders.Key(header), value);
    }
}
