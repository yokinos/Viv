using System.Reflection;
using Viv.Momo.Enums;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱的全部 SQL —— <b>手写，一次都不经过 EF</b>。
    ///
    /// <para>
    /// 列名一律不带引号：SqlServer 不区分大小写、PG 折叠成小写，同一句话在两端都成立，
    /// 所以只有真正需要方言的少数几条（建表 / 认领 / 清理）才按 provider 分叉。
    /// </para>
    ///
    /// <para>
    /// ⚠️ 所有时间参数都必须是 <see cref="DateTime.UtcNow"/> 派生（Kind=Utc）：
    /// PG 侧的列是 <c>TIMESTAMPTZ</c>，Npgsql 拒绝写入 Kind=Unspecified 的值。
    /// </para>
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
        /// 入队。走 <c>ExecuteSqlAsync</c>（写库上下文 + 转发 <c>_transaction</c>）——
        /// <b>这就是「与业务写同事务」的全部机关</b>，换成任何走读连接的执行方式都会让它失效。
        /// </summary>
        internal const string Insert =
            """
            INSERT INTO OutboxMessage
                (Id, MessageId, EventType, Payload, Status, RetryCount, NextRetryAt, LeaseUntil, OccurredAt, SentAt, LastError)
            VALUES
                (@Id, @MessageId, @EventType, @Payload, @Status, @RetryCount, @NextRetryAt, NULL, @OccurredAt, NULL, NULL)
            """;

        /// <summary>投递器崩溃 / 被杀后，卡在 Processing 的行靠这条复活。</summary>
        internal const string ReleaseExpiredLeases =
            """
            UPDATE OutboxMessage
            SET Status = 0, LeaseUntil = NULL
            WHERE Status = 1 AND LeaseUntil IS NOT NULL AND LeaseUntil <= @Now
            """;

        /// <summary>投递成功。</summary>
        internal const string MarkSent =
            """
            UPDATE OutboxMessage
            SET Status = 2, SentAt = @Now, LeaseUntil = NULL, LastError = NULL
            WHERE Id = @Id
            """;

        /// <summary>投递失败但要重试：退回 Pending 并把下次可投时刻推后（退避）。</summary>
        internal const string MarkPending =
            """
            UPDATE OutboxMessage
            SET Status = 0, RetryCount = @RetryCount, NextRetryAt = @NextRetryAt, LeaseUntil = NULL, LastError = @LastError
            WHERE Id = @Id
            """;

        /// <summary>重试耗尽，等人工介入（绝不静默丢）。</summary>
        internal const string MarkFailed =
            """
            UPDATE OutboxMessage
            SET Status = 3, RetryCount = @RetryCount, LeaseUntil = NULL, LastError = @LastError
            WHERE Id = @Id
            """;

        /// <summary>
        /// 原子认领一批待投递的行，并把它们置为 Processing + 加租约。
        ///
        /// <para>
        /// 这是<b>整个投递器的并发正确性所在</b>：多个实例同时扫同一张表，
        /// 靠数据库把这批行的所有权原子地判给其中一个 —— 不需要 Redis 锁，
        /// 也不存在「先查后改」的窗口（那会让同一条消息被两个实例各投一遍）。
        /// </para>
        ///
        /// <para>
        /// 两条都<b>必须打主库</b>：<c>IMomoDbContext</c> 上所有返回行的原生 SQL 方法
        /// （<c>FindListAsync&lt;T&gt;(sql)</c> 等）走的都是<b>读库</b>上下文，
        /// 拿它们跑认领在开启读写分离后会打到从库上 —— 所以这里改用
        /// <c>GetDbConnection(DbReadWriteType.Write)</c> 自己跑 Dapper。
        /// </para>
        /// </summary>
        internal static string ClaimBatch(DatabaseSourceType source) => source switch
        {
            DatabaseSourceType.PostgreSQL =>
                """
                UPDATE OutboxMessage SET Status = 1, LeaseUntil = @LeaseUntil
                WHERE Id IN (
                    SELECT Id FROM OutboxMessage
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
                UPDATE OutboxMessage WITH (READPAST)
                SET Status = 1, LeaseUntil = @LeaseUntil
                OUTPUT inserted.Id AS Id, inserted.MessageId AS MessageId,
                       inserted.EventType AS EventType, inserted.Payload AS Payload,
                       inserted.Status AS Status, inserted.RetryCount AS RetryCount,
                       inserted.NextRetryAt AS NextRetryAt, inserted.LeaseUntil AS LeaseUntil,
                       inserted.OccurredAt AS OccurredAt, inserted.SentAt AS SentAt,
                       inserted.LastError AS LastError
                WHERE Id IN (
                    SELECT TOP (@BatchSize) Id FROM OutboxMessage WITH (READPAST)
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
                DELETE FROM OutboxMessage
                WHERE Id IN (
                    SELECT Id FROM OutboxMessage
                    WHERE Status = 2 AND SentAt IS NOT NULL AND SentAt < @Cutoff
                    LIMIT @BatchSize
                )
                """,

            _ =>
                """
                DELETE FROM OutboxMessage
                WHERE Id IN (
                    SELECT TOP (@BatchSize) Id FROM OutboxMessage
                    WHERE Status = 2 AND SentAt IS NOT NULL AND SentAt < @Cutoff
                )
                """,
        };
    }
}
