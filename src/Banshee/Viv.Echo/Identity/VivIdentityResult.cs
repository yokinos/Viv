using Viv.Contracts.Models;

namespace Viv.Echo.Identity
{
    /// <summary>
    /// 身份头验签结果。
    /// <para>
    /// <see cref="Context"/> 为 <see langword="null"/> 表示「不该灌上下文」（未配密钥、或 AppId 不合法），
    /// 这**不是**失败；真正的失败（签名错、过期）由 <see cref="VivIdentityValidator"/> 抛
    /// <see cref="VivIdentityException"/>。
    /// </para>
    /// </summary>
    public readonly record struct VivIdentityResult(VivContextContent? Context, string? HolderId)
    {
        public static VivIdentityResult Empty => new(null, null);
    }
}
