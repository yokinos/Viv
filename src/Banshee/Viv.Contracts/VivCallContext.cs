using System.Threading;
using Viv.Delusion.Magic;

namespace Viv.Contracts
{
    /// <summary>
    /// 一次调用的运行时标识：链路追踪 Id + 分布式锁持有者 Id。
    ///
    /// <para>
    /// 两者都是 <see cref="AsyncLocal{T}"/> 静态上下文，使用场景重合 ——
    /// <c>RequestTrackMiddleware</c> 在请求入口设置，gRPC/MagicOnion 客户端拦截器在调用下游时读取，
    /// 消息消费者在处理前后写入与清理。原先分成 <c>LockHolderContext</c> 与 <c>VivTraceContext</c>
    /// 两个类，四份代码只为管两个 AsyncLocal 槽位，且「清一个忘一个」的坑迟早会踩。
    /// </para>
    ///
    /// <para>
    /// <b>为什么必须是静态的</b>：拦截器挂在单例客户端上、消息信封的 Id 是属性初始化器、
    /// <c>LockHolderContext</c> 在静态工具方法里被调用 —— 这些位置物理上拿不到容器。
    /// 需要 DI 的地方请用 <see cref="IVivContext"/>（租户上下文），那是另一回事。
    /// </para>
    ///
    /// <para>
    /// <b>与 <see cref="IVivContext"/> 的分工</b>：本类只装「这一次调用是谁发起的」这类瞬态标识，
    /// 不承担租户/用户身份。身份来自 <c>VivContextContent</c>，由网关验签后回填。
    /// </para>
    /// </summary>
    public static class VivCallContext
    {
        private static readonly AsyncLocal<string?> _traceId = new();
        private static readonly AsyncLocal<string?> _holderId = new();

        /// <summary>
        /// 当前异步流的 traceId。不在请求管道内（后台任务、启动阶段）时为 null。
        /// 由 <c>RequestTrackMiddleware</c> 在请求入口写入；网关生成的会写进转发请求头，
        /// gRPC 客户端拦截器也会把它带进下游 metadata。
        /// </summary>
        public static string? TraceId
        {
            get => _traceId.Value;
            set => _traceId.Value = value;
        }

        /// <summary>
        /// 当前锁持有者 Id。<b>getter 在值为空时会懒生成一个并写回</b>，
        /// 所以只要读过就一定拿得到非空值 —— 调用方不必判空。
        /// </summary>
        public static string HolderId
        {
            get
            {
                var current = _holderId.Value;
                if (!string.IsNullOrEmpty(current))
                {
                    return current;
                }

                current = IdMagic.NextIdString();
                _holderId.Value = current;
                return current;
            }
        }

        /// <summary>设置 traceId。由 <c>RequestTrackMiddleware</c> 在请求入口调用。</summary>
        public static void SetTraceId(string traceId) => _traceId.Value = traceId;

        /// <summary>
        /// 设置 holderId。上游验签通过（网关 / 对等服务 / 消息头）时采用上游值。
        /// </summary>
        public static void SetHolderId(string holderId) => _holderId.Value = holderId;

        /// <summary>
        /// 强制重新生成 holderId 并写回。请求入口、以及需要「换一个持有者」时调用。
        /// </summary>
        public static string GenerateHolderId()
        {
            var id = IdMagic.NextIdString();
            _holderId.Value = id;
            return id;
        }

        /// <summary>
        /// 重置 holderId（丢弃当前值，重新生成一个）。
        /// </summary>
        public static void ResetHolderId() => GenerateHolderId();

        /// <summary>
        /// 只清 holderId，保留 traceId。
        ///
        /// 消息消费者（<c>VivConsumer</c>）处理完一条消息后需要恢复「无持有者」状态，
        /// 但那条消息可能是在某个 HTTP 请求的异步流里被消费的 —— 连 traceId 一起清会让
        /// 后续日志丢掉链路。请求结束请用 <see cref="Clear"/>。
        /// </summary>
        public static void ClearHolderId() => _holderId.Value = null;

        /// <summary>
        /// 清空全部标识。<b>必须在请求结束时调用</b>：AsyncLocal 留着值不清，
        /// 线程池上接手的下一个请求会继承上一个的 traceId 与 holderId，
        /// 日志串到别人的链路上，锁持有者也变成别人。
        /// </summary>
        public static void Clear()
        {
            _traceId.Value = null;
            _holderId.Value = null;
        }
    }
}
