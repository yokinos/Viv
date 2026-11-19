using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Kinna.Contracts;

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

    /// <summary>
    /// 推送目标。用户目标用租户加用户查连接表；组目标带上租户和业务群 Id，
    /// 组名由投递流程用 <see cref="IKinnaKeys.GroupName"/> 生成，业务不参与拼装。
    /// </summary>
    public readonly record struct KinnaPushTarget(KinnaTargetKind Kind, long SubjectId, long UserId, long GroupId)
    {
        public static KinnaPushTarget ToUser(long subjectId, long userId)
            => new(KinnaTargetKind.User, subjectId, userId, 0);

        /// <summary>
        /// 投本机某个群组。
        ///
        /// 这里只收业务群 Id、不收现成的组名字符串：组名带租户前缀，业务自己拼一旦漏了租户，
        /// 跨租户串组不会报错，只会把消息发给不该看的人。前缀也只有 <see cref="IKinnaKeys"/>
        /// 知道，拼错了同样静默。
        /// </summary>
        public static KinnaPushTarget ToGroup(long subjectId, long groupId)
            => new(KinnaTargetKind.Group, subjectId, 0, groupId);
    }

    /// <summary>业务产出的推送计划。Kind 只当指标标签用，框架不解析。</summary>
    /// <param name="Kind">指标标签，框架不解析</param>
    /// <param name="Method">客户端方法名</param>
    /// <param name="Payload">载荷</param>
    /// <param name="Target">投递目标</param>
    /// <param name="AuthorId">署名作者，用于补投发送方的其它端</param>
    /// <param name="MirrorToSender">
    /// 单聊是否补投给发送方的**其它**端。
    ///
    /// 群聊不用开：发送方的每条连接本来就加在组里，组广播天然覆盖它。
    /// 单聊必须开，否则手机发出、PC 上看不到——投递目标只有接收方一个人。
    /// </param>
    /// <param name="SkipSenderClient">
    /// 是否把「发起这次请求的那个客户端」从投递目标里剔掉。
    ///
    /// 开着才有「服务端去重」：发起端已经在接口返回里拿到了结果（<c>MessageId</c> / <c>SentAt</c>），
    /// 再推一份就是同一屏出现两条，只能靠客户端按 Id 去重——那正是这里想省掉的事。
    /// 剔除之后每条消息对每条连接至多一次，客户端不需要任何去重逻辑。
    ///
    /// 关掉它的场景只有一个：发送方不用接口返回渲染自己的气泡，而是干等推送。
    /// 那时剔掉发起端会让它自己的消息不显示。
    /// </param>
    public sealed record KinnaPushPlan(
        string Kind,
        string Method,
        object Payload,
        KinnaPushTarget Target,
        long AuthorId = 0,
        bool MirrorToSender = false,
        bool SkipSenderClient = true);

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
