using System.Reflection;
using Viv.Momo.Enums;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱的全部 SQL —— 手写，一次都不经过 EF。
    ///
    /// 输入侧列名一律不带引号（INSERT 列名 / WHERE / SET）：SqlServer 不区分大小写、PG 折叠成小写，
    /// 同一句话在两端都成立，所以只有真正需要方言的少数几条（建表 / 认领 / 清理）才按 provider 分叉。
    /// 例外是 RETURNING / OUTPUT 的输出列别名 —— PG 侧必须带引号（<c>AS "Id"</c>），
    /// 否则折叠成小写后 Dapper 映射不回 POCO 的 PascalCase 属性。
    ///
    /// 所有时间参数都必须是 <see cref="DateTime.UtcNow"/> 派生（Kind=Utc）：
    /// PG 侧的列是 <c>TIMESTAMPTZ</c>，Npgsql 拒绝写入 Kind=Unspecified 的值。
    /// </summary>
    internal static class OutboxSql
    {
        private const string SqlServerDdlResource = "Viv.Outbox.Sql.OutboxMessage.sql";
        private const string PostgreSqlDdlResource = "Viv.Outbox.Sql.OutboxMessage.pg.sql";

        /// <summary>建表脚本（幂等）。与仓库里的 <c>Sql/*.sql</c> 是同一份文件，运行时直接读它，不存在第二份副本。</summary>
        internal static string CreateTable(DatabaseSourceType source)
            => ReadResource(source == DatabaseSourceType.PostgreSQL ? PostgreSqlDdlResource : SqlServerDdlResource);

        internal static string ReadResource(string name)
        {
            using var stream = typeof(OutboxSql).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"发件箱 SQL 资源缺失：{name}（程序集未把 Sql/*.sql 打成嵌入资源？）");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// 入队。走 <c>ExecuteSqlAsync</c>（写库上下文 + 转发 <c>_transaction</c>），
        /// 「与业务写同事务」全靠这一点，换成任何走读连接的执行方式都会失效。
        /// </summary>
        internal const string Insert =
            """
            INSERT INTO VivOutboxMessage
                (Id, MessageId, EventType, Payload, Status, RetryCount, NextRetryAt, LeaseUntil, OccurredAt, SentAt, LastError)
            VALUES
                (@Id, @MessageId, @EventType, @Payload, @Status, @RetryCount, @NextRetryAt, NULL, @OccurredAt, NULL, NULL)
            """;

        /// <summary>投递器崩溃 / 被杀后，卡在 Processing 的行靠这条复活。</summary>
        internal const string ReleaseExpiredLeases =
            """
            UPDATE VivOutboxMessage
            SET Status = 0, LeaseUntil = NULL
            WHERE Status = 1 AND LeaseUntil IS NOT NULL AND LeaseUntil <= @Now
            """;

        /// <summary>投递成功。</summary>
        internal const string MarkSent =
            """
            UPDATE VivOutboxMessage
            SET Status = 2, SentAt = @Now, LeaseUntil = NULL, LastError = NULL
            WHERE Id = @Id
            """;

        /// <summary>投递失败但要重试：退回 Pending 并把下次可投时刻推后（退避）。</summary>
        internal const string MarkPending =
            """
            UPDATE VivOutboxMessage
            SET Status = 0, RetryCount = @RetryCount, NextRetryAt = @NextRetryAt, LeaseUntil = NULL, LastError = @LastError
            WHERE Id = @Id
            """;

        /// <summary>重试耗尽，等人工介入（绝不静默丢）。</summary>
        internal const string MarkFailed =
            """
            UPDATE VivOutboxMessage
            SET Status = 3, RetryCount = @RetryCount, LeaseUntil = NULL, LastError = @LastError
            WHERE Id = @Id
            """;

        /// <summary>
        /// 原子认领一批待投递的行，置为 Processing 并加租约。并发正确性全在这一条：
        /// 多个实例同时扫同一张表，靠数据库把这批行的所有权原子地判给其中一个，
        /// 不存在「先查后改」的窗口（那会让同一条消息被两个实例各投一遍）。
        ///
        /// 必须打主库：<c>IMomoDbContext</c> 上所有返回行的原生 SQL 方法走的是读库上下文，
        /// 开启读写分离后会打到从库上，所以调用方改用 <c>GetDbConnection(DbReadWriteType.Write)</c> 自己跑 Dapper。
        /// </summary>
        internal static string ClaimBatch(DatabaseSourceType source) => source switch
        {
            DatabaseSourceType.PostgreSQL =>
                """
                UPDATE VivOutboxMessage SET Status = 1, LeaseUntil = @LeaseUntil
                WHERE Id IN (
                    SELECT Id FROM VivOutboxMessage
                    WHERE Status = 0 AND NextRetryAt <= @Now
                    ORDER BY NextRetryAt, Id
                    LIMIT @BatchSize
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING Id AS "Id", MessageId AS "MessageId", EventType AS "EventType",
                          Payload AS "Payload", Status AS "Status", RetryCount AS "RetryCount",
                          NextRetryAt AS "NextRetryAt", LeaseUntil AS "LeaseUntil",
                          OccurredAt AS "OccurredAt", SentAt AS "SentAt", LastError AS "LastError"
                """,

            // SQL Server 没有 SKIP LOCKED；READPAST 是同一件事的说法 —— 跳过被别人锁住的行，
            // 既不阻塞也不重复认领。
            _ =>
                """
                UPDATE VivOutboxMessage WITH (READPAST)
                SET Status = 1, LeaseUntil = @LeaseUntil
                OUTPUT inserted.Id AS Id, inserted.MessageId AS MessageId,
                       inserted.EventType AS EventType, inserted.Payload AS Payload,
                       inserted.Status AS Status, inserted.RetryCount AS RetryCount,
                       inserted.NextRetryAt AS NextRetryAt, inserted.LeaseUntil AS LeaseUntil,
                       inserted.OccurredAt AS OccurredAt, inserted.SentAt AS SentAt,
                       inserted.LastError AS LastError
                WHERE Id IN (
                    SELECT TOP (@BatchSize) Id FROM VivOutboxMessage WITH (READPAST)
                    WHERE Status = 0 AND NextRetryAt <= @Now
                    ORDER BY NextRetryAt, Id
                )
                """,
        };

        /// <summary>分批清理已发送且超过保留期的行（一批一次，调用方循环到没得删为止）。</summary>
        internal static string CleanupBatch(DatabaseSourceType source) => source switch
        {
            DatabaseSourceType.PostgreSQL =>
                """
                DELETE FROM VivOutboxMessage
                WHERE Id IN (
                    SELECT Id FROM VivOutboxMessage
                    WHERE Status = 2 AND SentAt IS NOT NULL AND SentAt < @Cutoff
                    LIMIT @BatchSize
                )
                """,

            _ =>
                """
                DELETE FROM VivOutboxMessage
                WHERE Id IN (
                    SELECT TOP (@BatchSize) Id FROM VivOutboxMessage
                    WHERE Status = 2 AND SentAt IS NOT NULL AND SentAt < @Cutoff
                )
                """,
        };

        /// <summary>待投（Pending=0）与失败（Failed=3）条数，给仪表盘/gauge 用。</summary>
        internal const string CountDepth =
            """
            SELECT
                COALESCE(SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END), 0) AS Pending,
                COALESCE(SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END), 0) AS Failed
            FROM VivOutboxMessage
            """;
    }

    internal sealed class OutboxDepth
    {
        public long Pending { get; set; }
        public long Failed { get; set; }
    }
}
