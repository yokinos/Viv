using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;

namespace Viv.Kinna.Dispatch
{
    /// <summary>推送目标类型。</summary>
    public enum KinnaTargetKind
    {
        /// <summary>按用户查本机连接表</summary>
        User = 0,

        /// <summary>投本机 SignalR 组</summary>
        Group = 1
    }

    /// <summary>推送目标。用户目标用租户加用户查连接，组目标直接用组名。</summary>
    public readonly record struct KinnaPushTarget(KinnaTargetKind Kind, long SubjectId, long UserId, string? GroupName)
    {
        public static KinnaPushTarget ToUser(long subjectId, long userId)
            => new(KinnaTargetKind.User, subjectId, userId, null);

        public static KinnaPushTarget ToGroup(string groupName)
            => new(KinnaTargetKind.Group, 0, 0, groupName);
    }

    /// <summary>业务产出的推送计划。Kind 只当指标标签用，框架不解析。</summary>
    public sealed record KinnaPushPlan(
        string Kind,
        string Method,
        object Payload,
        KinnaPushTarget Target,
        long AuthorId = 0);

    /// <summary>
    /// 调用者上下文。信封里没有身份时可以传 null，这时租户和署名全由计划决定。
    /// </summary>
    public sealed record KinnaPushContext(
        long AppId,
        long SubjectId,
        long UserId,
        string? TraceId,
        long TransportMessageId = 0)
    {
        /// <param name="content">信封里的上下文</param>
        /// <param name="transportMessageId">信封的消息 Id，事件体没带业务 Id 时兜底用</param>
        public static KinnaPushContext From(VivContextContent content, long transportMessageId = 0)
            => new(content.AppId, content.SubjectId, content.UserId, content.TraceId, transportMessageId);
    }

    /// <summary>投递结果。Skipped 表示目标不在本机，调用方按成功处理。</summary>
    public enum KinnaPushOutcome
    {
        Success = 0,
        Skipped = 1,
        Failed = 2,
        Dropped = 3
    }

    /// <summary>业务实现这个：把事件翻译成方法名、载荷和目标。</summary>
    public interface IKinnaPusher<in TEvent>
    {
        /// <returns>null 表示丢弃，会记 Dropped 指标。</returns>
        KinnaPushPlan? Plan(TEvent evt, KinnaPushContext? context);
    }

    /// <summary>通用的投递流程。</summary>
    public interface IKinnaMessageDispatcher<THub, TEvent> where THub : Hub
    {
        Task<KinnaPushOutcome> DispatchAsync(TEvent evt, KinnaPushContext? context, CancellationToken cancellationToken = default);
    }
}
