using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Viv.Contracts
{
    /// <summary>
    /// x-request-token HMAC-SHA256：载荷为 5 个 x-viv-* 头值（含 holder-id）+ unix 时间戳，格式 {unixSeconds}:{base64Sig}。
    /// HTTP 网关与 gRPC 拦截器共用，避免两套算法漂移。
    /// </summary>
    public static class VivRequestToken
    {
        /// <summary>签名有效期（秒）。超过则视为重放。</summary>
        public const long MaxReplayAgeSeconds = 300;

        /// <summary>允许的未来时钟偏差（秒）。超出则拒绝，避免把有效期拉到「未来时刻 + 5 分钟」。</summary>
        public const long MaxFutureSkewSeconds = 60;

        public static string Sign(string appId, string subjectId, string userId, string serviceName, string holderId, string secret, long? unixSeconds = null)
        {
            var timestamp = unixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return timestamp.ToString(CultureInfo.InvariantCulture) + ":" + ComputeSignature(appId, subjectId, userId, serviceName, holderId, secret, timestamp);
        }

        public static bool TryVerify(string? token, string appId, string subjectId, string userId, string serviceName, string holderId, string secret)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            var sep = token.IndexOf(':');
            if (sep <= 0 || sep >= token.Length - 1)
            {
                return false;
            }

            if (!long.TryParse(token.AsSpan(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (timestamp > now + MaxFutureSkewSeconds)
            {
                return false;
            }

            if (now - timestamp > MaxReplayAgeSeconds)
            {
                return false;
            }

            var expected = ComputeSignature(appId, subjectId, userId, serviceName, holderId, secret, timestamp);
            var provided = token[(sep + 1)..];
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(expected));
        }

        public static string ComputeSignature(string appId, string subjectId, string userId, string serviceName, string holderId, string secret, long timestamp)
        {
            var payload = string.Join('\n',
                appId ?? "",
                subjectId ?? "",
                userId ?? "",
                serviceName ?? "",
                holderId ?? "",
                timestamp.ToString(CultureInfo.InvariantCulture));

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
