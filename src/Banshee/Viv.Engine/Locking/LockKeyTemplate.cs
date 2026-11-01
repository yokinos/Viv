using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Viv.Engine.Locking
{
    /// <summary>
    /// <see cref="Viv.Contracts.Attributes.VivDistributedLockAttribute.KeyTemplate"/> 的解析与启动期校验。
    ///
    /// <para><b>占位符三种来源</b></para>
    /// <list type="bullet">
    /// <item><description>方法参数名：<c>{orderId}</c></description></item>
    /// <item><description>参数属性路径：<c>{request.OrderId}</c>（可多级，属性必须可读）</description></item>
    /// <item><description>上下文 token：<c>{tenantId}</c>（SubjectId）/ <c>{userId}</c> / <c>{appId}</c></description></item>
    /// </list>
    ///
    /// <para><b>用法</b></para>
    /// <code>
    /// [VivDistributedLock("order", KeyTemplate = "{tenantId}:{orderId}")]
    /// public Task RefundAsync(long orderId) =&gt; ...;
    /// // lock:aop:order:1001:42
    ///
    /// [VivDistributedLock("order", KeyTemplate = "{request.OrderId}")]
    /// public Task CancelAsync(CancelRequest request) =&gt; ...;
    /// </code>
    ///
    /// <para>模板即完整业务 key，不再自动补租户。启动期校验占位符与参数/属性是否对得上；
    /// 运行期任一占位符解析为空一律抛 <see cref="DistributedLockKeyException"/> —— 空 key 会退化成全局锁。</para>
    /// </summary>
    internal static class LockKeyTemplate
    {
        private const string TenantToken = "tenantId";
        private const string UserToken = "userId";
        private const string AppToken = "appId";

        private static readonly HashSet<string> _contextTokens = new(StringComparer.Ordinal)
        {
            TenantToken,
            UserToken,
            AppToken
        };

        private static readonly Regex _tokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// 启动期校验：占位符必须能对上参数名（或上下文 token），属性路径必须可读。
        /// </summary>
        public static void Validate(string template, ParameterInfo[] parameters, string methodDisplay)
        {
            var withoutTokens = _tokenRegex.Replace(template, string.Empty);
            if (withoutTokens.Contains('{') || withoutTokens.Contains('}'))
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {methodDisplay} 的 KeyTemplate 花括号不配对：{template}");
            }

            foreach (Match match in _tokenRegex.Matches(template))
            {
                var token = match.Groups[1].Value.Trim();
                if (token.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {methodDisplay} 的 KeyTemplate 含空占位符：{template}");
                }

                ValidateToken(token, parameters, methodDisplay);
            }
        }

        /// <summary>
        /// 运行期解析。任一占位符为空都直接抛，不做降级。
        /// </summary>
        public static string Resolve(string template, ParameterInfo[] parameters, object?[] arguments,
            long tenantId, long userId, long appId)
        {
            return _tokenRegex.Replace(template, match =>
            {
                var token = match.Groups[1].Value.Trim();
                var value = ResolveToken(token, parameters, arguments, tenantId, userId, appId);

                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new DistributedLockKeyException(
                        $"锁 Key 模板占位符 {{{token}}} 解析为空，模板：{template}");
                }

                return value;
            });
        }

        private static void ValidateToken(string token, ParameterInfo[] parameters, string methodDisplay)
        {
            if (_contextTokens.Contains(token)) return;

            var segments = token.Split('.');
            var parameter = parameters.FirstOrDefault(p =>
                string.Equals(p.Name, segments[0], StringComparison.Ordinal));

            if (parameter is null)
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {methodDisplay} 的 KeyTemplate 占位符 {{{token}}} 找不到同名参数。" +
                    "占位符只能取方法参数名、参数属性路径或 tenantId/userId/appId。");
            }

            var type = parameter.ParameterType;
            for (var i = 1; i < segments.Length; i++)
            {
                var property = type.GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance);
                if (property is null || !property.CanRead)
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {methodDisplay} 的占位符 {{{token}}} 在 {type.Name} 上找不到可读属性 {segments[i]}。");
                }

                type = property.PropertyType;
            }
        }

        private static string? ResolveToken(string token, ParameterInfo[] parameters, object?[] arguments,
            long tenantId, long userId, long appId)
        {
            switch (token)
            {
                case TenantToken:
                    return Format(tenantId);
                case UserToken:
                    return Format(userId);
                case AppToken:
                    return Format(appId);
            }

            var segments = token.Split('.');
            var index = Array.FindIndex(parameters, p =>
                string.Equals(p.Name, segments[0], StringComparison.Ordinal));

            if (index < 0 || index >= arguments.Length) return null;

            object? value = arguments[index];
            for (var i = 1; i < segments.Length && value is not null; i++)
            {
                value = value.GetType()
                    .GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(value);
            }

            return Format(value);
        }

        private static string? Format(object? value) => value switch
        {
            null => null,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}

