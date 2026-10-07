using System;
using System.Collections.Generic;
using System.Text.Json;
using Viv.Log;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// OtCapabilityBinding.AllowedSubjectIds（主体白名单）的解析与判定。
    ///
    /// 单独成类型是因为工具闭包会被 AgentFactory 缓存 60 秒：白名单必须**每次调用现算**，
    /// 装配期把当前 subjectId 定死，就等于让第一个请求决定所有人能不能用。
    /// 空白、空数组、坏 JSON 一律按"不限制"处理 —— 一条脏数据不该把工具全废掉。
    /// </summary>
    public static class SubjectAllowList
    {
        /// <summary>
        /// 主体是否被允许调用该能力：没配白名单、空数组、解析失败都算允许
        /// </summary>
        /// <param name="allowedSubjectIds">绑定上的 AllowedSubjectIds 原文（JSON 数组字符串）</param>
        /// <param name="subjectId">当前请求的主体 Id（IVivContext.SubjectId）</param>
        /// <param name="capabilityKey">工具键 / 子 Agent 键（只用于日志）</param>
        /// <param name="logger">日志</param>
        public static bool IsAllowed(string? allowedSubjectIds, long subjectId, string capabilityKey, ILoggerContract logger)
        {
            if (string.IsNullOrWhiteSpace(allowedSubjectIds)) return true;

            if (!TryParse(allowedSubjectIds, out var ids))
            {
                logger.Warning("能力绑定的 AllowedSubjectIds 不是合法的主体 Id 数组，按不限制处理：{0}，{1}",
                    capabilityKey, allowedSubjectIds);
                return true;
            }

            // 空数组 = 不限制（与"空白"同一语义）
            return ids.Count == 0 || ids.Contains(subjectId);
        }

        /// <summary>
        /// 写接口用的校验：白名单文本是不是"合法主体 Id 数组"。
        /// 判定口径与 <see cref="IsAllowed"/> 完全一致，避免写进去的东西运行时按"不限制"处理。
        /// </summary>
        /// <param name="allowedSubjectIds">绑定上的 AllowedSubjectIds 原文（空白 = 不限制，合法）</param>
        public static bool IsValid(string? allowedSubjectIds)
            => string.IsNullOrWhiteSpace(allowedSubjectIds) || TryParse(allowedSubjectIds, out _);

        /// <summary>
        /// 拒绝时回给模型（并写进留痕 ErrorMessage）的文案
        /// </summary>
        /// <param name="subjectId">当前请求的主体 Id</param>
        public static string DenyMessage(long subjectId) => $"无权限调用该工具（subjectId={subjectId}）";

        /// <summary>
        /// 解析 JSON 数组：元素认数字或数字字符串；结构不对、元素不是数字都算失败
        /// </summary>
        private static bool TryParse(string json, out HashSet<long> ids)
        {
            ids = [];

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return false;

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Number when element.TryGetInt64(out var number):
                            ids.Add(number);
                            break;
                        case JsonValueKind.String when long.TryParse(element.GetString(), out var text):
                            ids.Add(text);
                            break;
                        default:
                            return false;
                    }
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
