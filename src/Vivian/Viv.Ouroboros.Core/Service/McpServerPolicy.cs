using System;
using System.Collections.Generic;
using System.Text.Json;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// <see cref="OtMcpServer"/> 上三个"策略列"的解析与判定：AllowedTools（暴露白名单）、
    /// AlwaysRequireToolNames / NeverRequireToolNames（审批名单）。
    ///
    /// 单独成类型是为了让策略只有一份口径，且与 <see cref="SubjectAllowList"/> 同一条脏数据原则：
    /// 空白、空数组、坏 JSON 一律按"这条策略没配"处理并记 Warning —— 一条脏 JSON 不该把整个服务废掉。
    /// </summary>
    public static class McpServerPolicy
    {
        /// <summary>
        /// 解析一个 JSON 字符串数组列。空白 → 空集；坏 JSON / 不是数组 / 元素不是字符串 → 空集 + Warning。
        /// 空集在调用方一律读作"这条策略没配"。
        /// </summary>
        /// <param name="json">列原文</param>
        /// <param name="serverName">服务名（只用于日志）</param>
        /// <param name="column">列名（只用于日志）</param>
        /// <param name="logger">日志</param>
        public static IReadOnlySet<string> ParseNames(string? json, string serverName, string column, ILoggerContract logger)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return names;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger.Warning("MCP 服务的 {0} 不是 JSON 数组，按未配置处理：{1}", column, serverName);
                    return names;
                }

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        logger.Warning("MCP 服务的 {0} 里混了非字符串元素，整列按未配置处理：{1}", column, serverName);
                        return new HashSet<string>(StringComparer.Ordinal);
                    }

                    var name = element.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }

                return names;
            }
            catch (JsonException)
            {
                logger.Warning("MCP 服务的 {0} 不是合法 JSON，按未配置处理：{1}", column, serverName);
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// 某个工具要不要人工审批。
        ///
        /// 优先级（从高到低）：
        /// <list type="number">
        /// <item><description>绑定的 <c>RequiresApproval</c>（非空即覆盖服务自身的 ApprovalMode：true 当 All、false 当 None）。</description></item>
        /// <item><description><c>AlwaysRequireToolNames</c> 命中 → 要审批。它同时命中 <c>NeverRequireToolNames</c> 时按"要审批"算（fail-closed），并记 Warning 指出配置自相矛盾。</description></item>
        /// <item><description><c>NeverRequireToolNames</c> 命中 → 免审批。它是操作员显式开的例外口，也是 <c>All</c> 模式下唯一能豁免只读工具的手段。</description></item>
        /// <item><description>都没有 → 看 <c>ApprovalMode</c>：<c>All</c> 要审批，<c>None</c>/<c>ByList</c> 不要。</description></item>
        /// </list>
        ///
        /// 两个名单在任何模式下都生效（而不只在 <c>ByList</c> 下）：列名本身就是"一律要审 / 一律免审"的绝对说法，
        /// 且本实现上线时 <c>OtMcpServer</c> 还没有任何历史数据，放开不会改变既有行为。
        /// "同时在两个名单里"按 fail-closed 解释：审批是安全闸门，配置矛盾时宁可多问一次。
        /// </summary>
        /// <param name="mode">服务自身的审批模式</param>
        /// <param name="bindingOverride">绑定上的审批覆盖（空 = 沿用服务设置）</param>
        /// <param name="toolName">服务侧的原始工具名（名单里写的是它，不是加前缀后的模型可见名）</param>
        /// <param name="always">AlwaysRequireToolNames 解析结果</param>
        /// <param name="never">NeverRequireToolNames 解析结果</param>
        /// <param name="serverName">服务名（只用于日志）</param>
        /// <param name="logger">日志</param>
        public static bool RequiresApproval(EmMcpApprovalMode mode, bool? bindingOverride, string toolName,
            IReadOnlySet<string> always, IReadOnlySet<string> never, string serverName, ILoggerContract logger)
        {
            if (bindingOverride.HasValue) return bindingOverride.Value;

            if (always.Contains(toolName))
            {
                if (never.Contains(toolName))
                    logger.Warning("MCP 工具的审批名单自相矛盾（同时命中 Always 与 Never），按需要审批处理：{0} → {1}",
                        serverName, toolName);
                return true;
            }

            if (never.Contains(toolName)) return false;

            return mode == EmMcpApprovalMode.All;
        }
    }
}
