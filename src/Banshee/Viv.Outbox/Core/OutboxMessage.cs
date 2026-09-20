namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱表 <c>VivOutboxMessage</c> 的实体行。
    /// <list type="bullet">
    /// <item><description>实体设计：纯普通POCO类型，不实现 <c>IEntity</c>、<c>ITenant</c>，不注册到 <c>EntityTypeOptions</c>。</description></item>
    /// <item><description>数据访问：本表全部采用手写SQL，完全不经过EF Core。若继承IEntity会被EF扫描并自动套用字段命名规则，与手写DDL的表结构冲突；若继承ITenant，会被全局查询过滤器拦截，而投递后台运行在无租户作用域。</description></item>
    /// <item><description>字段约定：属性名与数据库列名完全一致（PascalCase）。SQL 输入侧（INSERT 列名 / WHERE / SET）无需加标识符引号 —— SqlServer 不区分大小写、PG 折叠成小写；输出列别名则必须加引号（PG 的 <c>RETURNING Id AS "Id"</c>），否则折叠成小写后 Dapper 映射不回 PascalCase 属性。</description></item>
    /// </list>
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
