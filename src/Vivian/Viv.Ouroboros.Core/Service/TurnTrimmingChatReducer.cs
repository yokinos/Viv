using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 按"轮"裁剪对话上下文的 <see cref="IChatReducer"/>：只保留最近 N 轮 + 开头的系统消息。
    ///
    /// 为什么不用现成的 <c>MessageCountingChatReducer</c>（Microsoft.Extensions.AI 10.10.0）：
    /// 它把**所有**带工具内容的消息整条丢掉（实测 14 条进、工具调用与结果一条不剩），
    /// 而本项目的模型每个工具回环都会把"调用 + 结果"重新送一遍，丢了就等于模型在
    /// 看不到工具结果的情况下作答。它和 <c>SummarizingChatReducer</c>、<c>ReducingChatClient</c>
    /// 还都标着 <c>[Experimental("MEAI001")]</c>（"仅用于评估，将来可能被改或删"），
    /// 引用它们会引入新的 MEAI001 警告。
    ///
    /// 本实现的两条不变式：
    /// 1. 轮边界只认 <see cref="ChatRole.User"/> 消息，所以一个回合内的东西不会被拦腰切断；
    /// 2. 窗口起点往前的**连续工具消息**一律并进来（<see cref="HasToolContent"/>），
    ///    包括审批续跑时那条"用户角色的 ToolApprovalResponseContent"，免得模型看到
    ///    "没有调用的结果"或"没有结果的调用"。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public sealed class TurnTrimmingChatReducer : IChatReducer, IDependency
    {
        private readonly IOuroborosConfig _config;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="config">配置读取（每轮最多保留多少轮）</param>
        /// <param name="logger">日志</param>
        public TurnTrimmingChatReducer(IOuroborosConfig config, ILoggerContract logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            var all = messages as IList<ChatMessage> ?? messages.ToList();
            if (all.Count == 0) return all;

            var maxTurns = await _config.GetMaxRetainedTurnsAsync(cancellationToken).ConfigureAwait(false);
            if (maxTurns <= 0) return all;

            var cut = FindCut(all, maxTurns);
            if (cut <= 0) return all;

            _logger.Info("上下文已裁剪：{0} 条 → {1} 条（保留最近 {2} 轮 + 系统消息）",
                all.Count, all.Count - cut, maxTurns);

            return all.Skip(cut).ToList();
        }

        /// <summary>
        /// 算出保留窗口的起点下标；0 表示不裁（轮数还没到上限，或窗口会退化成空）
        /// </summary>
        private static int FindCut(IList<ChatMessage> all, int maxTurns)
        {
            // 系统消息永远保留，且不占轮数（提示词通常走 Instructions，这里只是兜住显式塞进来的那种）
            var head = 0;
            while (head < all.Count && all[head].Role == ChatRole.System) head++;

            // 从后往前数第 maxTurns 条 user 消息 = 窗口起点
            var seen = 0;
            var cut = head;
            for (var i = all.Count - 1; i >= head; i--)
            {
                if (all[i].Role != ChatRole.User) continue;
                if (++seen != maxTurns) continue;

                cut = i;
                break;
            }

            if (cut <= head) return 0;

            // 工具调用/审批/结果跨消息成组，切开会让模型看到半截；整组并进保留窗口
            while (cut > head && HasToolContent(all[cut - 1])) cut--;

            // 并到窗口起点贴着系统消息了：再往前没有可丢的内容，退回"不裁"更安全
            return cut <= head ? 0 : cut;
        }

        /// <summary>这条消息是不是工具调用链的一部分（调用、审批请求/应答、结果）</summary>
        private static bool HasToolContent(ChatMessage message)
            => message.Contents.Any(x => x is FunctionCallContent or FunctionResultContent
                or ToolApprovalRequestContent or ToolApprovalResponseContent);
    }
}
