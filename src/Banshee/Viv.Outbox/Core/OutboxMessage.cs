namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱表 <c>VivOutboxMessage</c> 的行。
    ///
    /// 刻意是普通 POCO：不实现 <c>IEntity</c>、不实现 <c>ITenant</c>、不注册进 <c>EntityTypeOptions</c>。
    /// 这张表全走手写 SQL，一次都不经过 EF —— 继承 IEntity 会被 EF 扫到并套用命名约定，
    /// 跟手写 DDL 的表名列名对不上；继承 ITenant 会被全局查询过滤器接管，
    /// 而投递器跑在无租户的后台作用域里。
    ///
    /// 属性名与列名逐字对应（PascalCase），SQL 里一律不加引号。
    /// </summary>
    public class OutboxMessage
    {
        /// <summary>主键，由 <c>IdMagic.NextId()</c> 生成</summary>
        public long Id { get; set; }

        /// <summary>
        /// 消息 Id —— 消费端 <c>nana:{ServiceName}:{EventType}:{MessageId}</c> 消费锁的去重键。
        /// 本列写一次就不再改，重投时以它为准钉回信封。
        /// </summary>
        public long MessageId { get; set; }

        /// <summary>事件类型的 <c>FullName</c>，投递时据此重建 <c>NanaEnvelope&lt;T&gt;</c></summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary><c>NanaEnvelope&lt;T&gt;</c> 的 JSON</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>见 <see cref="OutboxStatus"/></summary>
        public OutboxStatus Status { get; set; }

        /// <summary>已重试次数</summary>
        public int RetryCount { get; set; }

        /// <summary>下次可投递时刻（UTC）</summary>
        public DateTime NextRetryAt { get; set; }

        /// <summary>租约到期时刻（UTC）。超过它仍处于 Processing 的行会被重新认领 —— 崩溃恢复靠它。</summary>
        public DateTime? LeaseUntil { get; set; }

        /// <summary>入队时刻（UTC）</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>投递成功时刻（UTC）</summary>
        public DateTime? SentAt { get; set; }

        /// <summary>最近一次投递失败的原因（截断到列宽）</summary>
        public string? LastError { get; set; }
    }
}
