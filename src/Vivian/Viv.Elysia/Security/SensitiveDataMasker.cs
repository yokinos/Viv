using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Viv.Elysia.Security
{
    /// <summary>
    /// 操作日志落库前的脱敏。
    ///
    /// 登录 / 改密 / 刷新令牌这些请求的 body 里带着口令和刷新令牌，原样发进消息队列
    /// 等于把一份长期凭证复制到了日志系统（消费端、ES、备份全都有一份）。
    /// 这里按字段名打码，只处理「值」，不动结构，排查时仍能看出传了哪些字段。
    /// </summary>
    internal static class SensitiveDataMasker
    {
        internal const string MaskValue = "***";

        /// <summary>
        /// 命中的字段名片段（比对前会把字段名压成小写字母数字，所以 api_key / apiKey 都能命中 apikey）。
        /// 宁可多打几个码：日志少一个字段无所谓，多一份明文口令是事故。
        /// </summary>
        private static readonly string[] _sensitiveFragments =
        [
            "password", "passwd", "pwd", "secret", "token", "credential",
            "privatekey", "apikey", "salt", "signature"
        ];

        /// <summary>
        /// 对请求体脱敏：JSON 按字段名递归处理，表单按 key=value 处理，其余原样返回。
        /// </summary>
        internal static string? Mask(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    var node = JsonNode.Parse(raw);
                    return MaskNode(node)?.ToJsonString() ?? MaskValue;
                }
                catch (JsonException)
                {
                    // 声明是 JSON 却解析不了：按分隔符再试一次，仍是原样就整段打码 ——
                    // 结构坏掉的 body 里也可能有口令，不能因为解析失败就放行
                    var fallback = MaskDelimited(raw);
                    return fallback == raw ? MaskValue : fallback;
                }
            }

            return MaskDelimited(raw);
        }

        private static JsonNode? MaskNode(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    // 先取 key 快照再改，直接在枚举里赋值会抛 InvalidOperationException
                    foreach (var key in obj.Select(kv => kv.Key).ToList())
                    {
                        if (IsSensitive(key))
                        {
                            obj[key] = MaskValue;
                        }
                        else
                        {
                            MaskNode(obj[key]);
                        }
                    }

                    return obj;

                case JsonArray array:
                    foreach (var item in array)
                    {
                        MaskNode(item);
                    }

                    return array;

                default:
                    return node;
            }
        }

        /// <summary>
        /// 非 JSON 的兜底：只处理 <c>k=v&amp;k=v</c> 这种形态（表单 / query），
        /// 没命中敏感字段就原样返回，避免把普通文本日志改脏。
        /// </summary>
        private static string MaskDelimited(string raw)
        {
            if (!raw.Contains('=')) return raw;

            var parts = raw.Split('&');
            var changed = false;

            for (var i = 0; i < parts.Length; i++)
            {
                var separator = parts[i].IndexOf('=');
                if (separator <= 0) continue;

                var key = parts[i][..separator];
                if (!IsSensitive(key)) continue;

                parts[i] = string.Concat(key, "=", MaskValue);
                changed = true;
            }

            return changed ? string.Join('&', parts) : raw;
        }

        private static bool IsSensitive(string key)
        {
            var normalized = Normalize(key);
            if (normalized.Length == 0) return false;

            foreach (var fragment in _sensitiveFragments)
            {
                if (normalized.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>只留小写字母数字：New_Password / new-password / newPassword 归一到同一个词。</summary>
        private static string Normalize(string key)
        {
            Span<char> buffer = stackalloc char[key.Length];
            var length = 0;

            foreach (var c in key)
            {
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(buffer[..length]);
        }
    }
}
