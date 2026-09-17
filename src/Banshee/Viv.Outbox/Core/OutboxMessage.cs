namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱表 <c>OutboxMessage</c> 的行。
    ///
    /// <para>
    /// ⚠️ <b>刻意是普通 POCO —— 不实现 <c>IEntity</c>、不实现 <c>ITenant</c>、不注册进
    /// <c>EntityTypeOptions</c>。</b> 这张表全部经手写 SQL 读写，一次都不经过 EF：
    /// 一旦继承 <c>IEntity</c> 被 EF 扫到，表名/列名的命名约定（蛇形 vs PascalCase）立刻漂移，
    /// 手写 DDL 与 EF 建的表就对不上了；继承 <c>ITenant</c> 还会被全局过滤器接管 ——
    /// 而投递器跑在后台作用域，那里「有上下文但没有租户」会让过滤器静默只匹配
    /// <c>TenantId = 0</c> 的行。有防回归测试钉死这两点。
    /// </para>
    ///
    /// <para>
    /// 属性名与列名<b>逐字对应</b>（PascalCase）。列名在 SQL 里一律不带引号：
    /// SqlServer 不区分大小写、PG 折叠成小写，两端行为一致。
    /// </para>
    /// </summary>
    public class OutboxMessage
    {
        /// <summary>主键，由 <c>IdMagic.NextId()</c> 生成（无 IDENTITY，手写 INSERT 不依赖回填）</summary>
        public long Id { get; set; }

        /// <summary>
        /// 消息 Id —— 消费端 <c>nana:{ServiceName}:{EventType}:{MessageId}</c> 消费锁的去重键。
        /// 只给排查用，<b>真相以 Payload 里的为准</b>（本列写一次就不再改）。
        /// </summary>
        public long MessageId { get; set; }

        /// <summary>事件类型的程序集限定名，投递时据此重建 <c>NanaEnvelope&lt;T&gt;</c></summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary><c>NanaEnvelope&lt;T&gt;</c> 的 JSON（含 MessageId / Context / CreatedAt / ReDeliverCount）</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>见 <see cref="OutboxStatus"/></summary>
        public OutboxStatus Status { get; set; }

        /// <summary>已重试次数</summary>
        public int RetryCount { get; set; }

        /// <summary>下次可投递时刻（UTC）。退避重投就是把这个时间往后推。</summary>
        public DateTime NextRetryAt { get; set; }

        /// <summary>租约到期时刻（UTC）。超过它仍处于 Processing 的行会被重新认领 —— 崩溃恢复靠它。</summary>
        public DateTime? LeaseUntil { get; set; }

        /// <summary>入队时刻（UTC）。与延迟投递无关。</summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>投递成功时刻（UTC）</summary>
        public DateTime? SentAt { get; set; }

        /// <summary>最近一次投递失败的原因（截断到列宽）</summary>
        public string? LastError { get; set; }
    }
}
