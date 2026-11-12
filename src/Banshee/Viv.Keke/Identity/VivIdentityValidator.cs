using System.Globalization;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;

namespace Viv.Keke.Identity
{
    /// <summary>
    /// x-viv-* 身份头的验签与还原（服务端侧）。与 <see cref="VivIdentityHeaders"/> 配对。
    ///
    /// 规则与原先 gRPC 服务端拦截器完全一致，只是抽成传输无关的一层：
    /// <list type="bullet">
    /// <item><description>未配 <c>InternalToken</c>：不验签、不灌上下文（本地开发方便，保持旧行为）。</description></item>
    /// <item><description>配了密钥：签名不匹配 / 过期 / 缺签名一律抛 <see cref="VivIdentityException"/>，不区分原因。</description></item>
    /// <item><description>验签通过但 AppId 非正：返回空结果（调用继续，上下文保持空）。</description></item>
    /// </list>
    /// </summary>
    public static class VivIdentityValidator
    {
        /// <summary>
        /// 验签并还原上下文。<paramref name="readHeader"/> 由适配层提供（gRPC 读 <c>Metadata</c>，
        /// MagicOnion 读 <c>ServiceContext.CallContext.RequestHeaders</c>）。
        /// </summary>
        public static VivIdentityResult Validate(Func<string, string?> readHeader, VivInternalTokenOptions tokenOptions)
        {
            ArgumentNullException.ThrowIfNull(readHeader);
            ArgumentNullException.ThrowIfNull(tokenOptions);

            var secret = tokenOptions.InternalToken;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return VivIdentityResult.Empty;
            }

            // 用头上的原始字符串验签：UserId 写成 "0" 也要能和客户端签出来的载荷对上，
            // 所以不能先 parse 再回填。
            var appIdRaw = readHeader(VivHeaderContract.AppId) ?? string.Empty;
            var subjectRaw = readHeader(VivHeaderContract.SubjectId) ?? string.Empty;
            var userRaw = readHeader(VivHeaderContract.UserId) ?? string.Empty;
            var serviceName = readHeader(VivHeaderContract.ServiceName) ?? string.Empty;
            var holderRaw = readHeader(VivHeaderContract.HolderId) ?? string.Empty;
            var token = readHeader(VivHeaderContract.InnerRequestToken);

            if (!VivRequestToken.TryVerify(token, appIdRaw, subjectRaw, userRaw, serviceName, holderRaw, secret))
            {
                throw new VivIdentityException("未通过内部签名校验");
            }

            if (!long.TryParse(appIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) || appId <= 0)
            {
                return VivIdentityResult.Empty;
            }

            var holderId = string.IsNullOrWhiteSpace(holderRaw) ? null : holderRaw;

            return new VivIdentityResult(
                new VivContextContent
                {
                    AppId = appId,
                    SubjectId = ReadPositiveLong(subjectRaw),
                    UserId = ReadPositiveLong(userRaw),
                    HolderId = holderId
                },
                holderId);
        }

        private static long ReadPositiveLong(string raw)
            => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : 0;
    }
}
