using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 管理接口的 JSON 列校验：坏 JSON 落库只会在运行期变成一句 Warning 然后被静默跳过，
    /// 所以写入口一律先拦住。这里只判"形状对不对"，不校验 JSON Schema 语义。
    /// </summary>
    public static class AdminJson
    {
        /// <summary>
        /// 是不是合法 JSON 对象（空值算通过：这些列本来就允许留空）
        /// </summary>
        /// <param name="json">待校验的 JSON 文本</param>
        public static bool IsJsonObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;

            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// 是不是合法的字符串数组（空值算通过）
        /// </summary>
        /// <param name="json">待校验的 JSON 文本</param>
        public static bool IsStringArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return false;

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String) return false;
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// 是不是合法 JSON（数组或对象都算，用于只要求"是 JSON"的列）
        /// </summary>
        /// <param name="json">待校验的 JSON 文本</param>
        public static bool IsJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;

            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
