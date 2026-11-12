using System.Globalization;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;

namespace Viv.Keke.Identity
{
    /// <summary>
    /// x-viv-* 身份头的构造（客户端侧）。与 <see cref="VivIdentityValidator"/> 配对：
    /// 这里出一个 <see cref="VivIdentityValues"/>，gRPC / MagicOnion 的适配层各自把它写进自己的 metadata。
    /// </summary>
    public static class VivIdentityHeaders
    {
        /// <summary>
        /// 按当前 <see cref="IVivContext"/> 构造身份头值；配置了 <c>InternalToken</c> 才签 <c>x-request-token</c>。
        /// 未配密钥时不签（与旧拦截器行为一致：本地开发不验签）。
        /// </summary>
        public static VivIdentityValues Build(IVivContext context, VivInternalTokenOptions tokenOptions)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Build(context.VivContextAccessor.Current, tokenOptions);
        }

        /// <summary>
        /// 按上下文快照构造身份头值。<paramref name="snapshot"/> 为 null 时按全 0 处理（未登录 / 无租户）。
        /// MagicOnion 客户端 Filter 用这个重载：它挂在单例客户端上，只能拿 AsyncLocal 访问器，
        /// 不能捕获 Scoped 的 <see cref="IVivContext"/>。
        /// </summary>
        public static VivIdentityValues Build(VivContextContent? snapshot, VivInternalTokenOptions tokenOptions)
        {
            ArgumentNullException.ThrowIfNull(tokenOptions);

            var values = new VivIdentityValues(
                (snapshot?.AppId ?? 0).ToString(CultureInfo.InvariantCulture),
                (snapshot?.SubjectId ?? 0).ToString(CultureInfo.InvariantCulture),
                (snapshot?.UserId ?? 0).ToString(CultureInfo.InvariantCulture),
                tokenOptions.ServiceName ?? string.Empty,
                LockHolderContext.CurrentHolderId);

            var secret = tokenOptions.InternalToken;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return values;
            }

            return values with
            {
                RequestToken = VivRequestToken.Sign(
                    values.AppId,
                    values.SubjectId,
                    values.UserId,
                    values.ServiceName,
                    values.HolderId,
                    secret)
            };
        }

        /// <summary>头名统一小写：gRPC metadata 写盘要求小写，HTTP 侧大小写不敏感。</summary>
        public static string Key(string headerName) => headerName.ToLowerInvariant();
    }
}
