using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.EventContracts.Ouroboros;
using Viv.Log;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Ouroboros.Core.IService;

using Viv.Entity.Enums;

namespace Viv.Ouroboros.Worker.Consumers
{
    /// <summary>
    /// 消费 <see cref="OuroborosTurnEvent"/>：按事件里的用户消息 Id 认领那一轮并跑完。
    ///
    /// 必须继承 <see cref="VivConsumer{T}"/>：基类 HandleAsync 会把信封里的上下文水合进 IVivContext，
    /// 主体才与投递侧一致；写成裸 Wolverine handler 主体会退化成 0，AgentChatService 的会话越权校验直接拒掉每一轮。
    ///
    /// 构造只吃 VivConsumerDependency 聚合 + IServiceScopeFactory，业务服务每轮现开作用域解析 ——
    /// Wolverine 的代码生成只认 IServiceCollection 里的注册，而 Ouroboros 的业务服务注册在 Autofac 里，
    /// 直接写进构造参数会被判成 missing registered dependencies，每一条消息都进死信。
    /// 每轮新开作用域同时保证 Scoped 仓储（IMomoDbContext）不跨消息复用。
    /// </summary>
    public class OuroborosTurnConsumer : VivConsumer<OuroborosTurnEvent>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dependency">框架依赖聚合</param>
        /// <param name="scopeFactory">作用域工厂，用来解析业务服务</param>
        public OuroborosTurnConsumer(VivConsumerDependency dependency, IServiceScopeFactory scopeFactory)
            : base(dependency)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// 定位用户消息 → 幂等判断 → 跑这一轮
        /// </summary>
        /// <param name="envelope">消息信封</param>
        /// <param name="cancellationToken">取消令牌</param>
        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<OuroborosTurnEvent> envelope, CancellationToken cancellationToken = default)
        {
            var content = envelope.Content!;

            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentStore>();
            var chat = scope.ServiceProvider.GetRequiredService<IAgentChatService>();

            var conversation = await store.GetConversationAsync(content.ConversationKey);
            if (conversation is null)
            {
                // 会话不存在是终态，重试多少次都一样，直接确认掉别耗重试次数
                _logger.Warning("会话不存在，消息丢弃：{0}", content.ConversationKey);
                return SubscribeResult.Success();
            }

            var messages = await store.ListMessagesAsync(conversation.Id);
            var userMessage = messages.Find(x => x.Id == content.UserMessageId);
            if (userMessage is null)
            {
                _logger.Warning("用户消息不存在，消息丢弃：会话 {0}，消息 {1}", content.ConversationKey, content.UserMessageId);
                return SubscribeResult.Success();
            }

            // 幂等：这条用户消息之后已经有助手回复，说明这一轮跑过了 —— 重投递直接跳过，不能再跑一遍
            if (messages.Exists(x => x.Seq > userMessage.Seq && x.Role == EmMessageRole.Assistant))
            {
                _logger.Info("用户消息 {0} 之后已有助手回复，跳过本轮（幂等）", content.UserMessageId);
                return SubscribeResult.Success();
            }

            // 带上信封的 MessageId：落库段会用它向 IVivInbox 认领消息级幂等键（与助手消息同事务）
            var result = await chat.RunQueuedTurnAsync(content.ConversationKey, content.UserMessageId, envelope.MessageId, cancellationToken);
            if (result.Code != (int)ApiResultCode.Success)
            {
                // 抛出去交给 Wolverine 的 RetryWithCooldown 重试、耗尽进死信；
                // 在这里吞掉等于这笔消息静默丢失，绝不允许
                throw new VivRequeueException($"会话 {content.ConversationKey} 第 {content.UserMessageId} 轮跑失败：{result.Message}");
            }

            _logger.Info("会话 {0} 的用户消息 {1} 已消费完成", content.ConversationKey, content.UserMessageId);
            return SubscribeResult.Success();
        }
    }
}
