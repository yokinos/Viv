using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 把 <see cref="IChatReducer"/> 接进聊天客户端管道的装饰器：每次发请求前先把消息列表交给
    /// 裁剪器过一遍。用 <see cref="DelegatingChatClient"/> 而不是 <c>ReducingChatClient</c>
    /// （后者是 <c>[Experimental("MEAI001")]</c>），其余行为（GetService 透传、Dispose）由基类照旧。
    ///
    /// 位置：贴在最内层的供应商客户端外面。这样它才在 MAF 的工具回环**内侧**，
    /// 每次回环都看得到完整的"调用 + 结果"，从而能把整组一起保留（见 <see cref="TurnTrimmingChatReducer"/>）。
    /// </summary>
    public sealed class ReducerChatClient : DelegatingChatClient
    {
        private readonly IChatReducer _reducer;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="innerClient">供应商的真实客户端</param>
        /// <param name="reducer">裁剪器</param>
        public ReducerChatClient(IChatClient innerClient, IChatReducer reducer) : base(innerClient)
            => _reducer = reducer;

        /// <inheritdoc />
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var reduced = await _reducer.ReduceAsync(messages, cancellationToken).ConfigureAwait(false);
            return await base.GetResponseAsync(reduced, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reduced = await _reducer.ReduceAsync(messages, cancellationToken).ConfigureAwait(false);

            await foreach (var update in base.GetStreamingResponseAsync(reduced, options, cancellationToken)
                               .ConfigureAwait(false))
                yield return update;
        }
    }
}
