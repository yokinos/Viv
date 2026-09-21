using Viv.Momo.Enums;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// Inbox 手写 SQL。与发件箱同一口径：列名不带引号，DDL 按 provider 分叉。
    /// </summary>
    internal static class InboxSql
    {
        private const string SqlServerDdlResource = "Viv.Outbox.Sql.InboxMessage.sql";
        private const string PostgreSqlDdlResource = "Viv.Outbox.Sql.InboxMessage.pg.sql";

        internal static string CreateTable(DatabaseSourceType source)
            => OutboxSql.ReadResource(source == DatabaseSourceType.PostgreSQL ? PostgreSqlDdlResource : SqlServerDdlResource);

        internal const string Insert =
            """
            INSERT INTO VivInboxMessage (ServiceName, MessageId, AcceptedAt)
            VALUES (@ServiceName, @MessageId, @AcceptedAt)
            """;

        /// <summary>
        /// 分批清理超过保留期的行（一批一次，调用方循环到没得删为止）。
        ///
        /// 表的主键是 (ServiceName, MessageId) 复合键，没有单列 Id 可拿来圈批 —— 所以不能照抄发件箱那条
        /// 「Id IN (SELECT ...)」。SQL Server 用 DELETE TOP，PostgreSQL 没有 DELETE LIMIT，
        /// 改用行值 IN 子查询。两端都落在 AcceptedAt 上，建表脚本给它配了索引。
        /// </summary>
        internal static string CleanupBatch(DatabaseSourceType source) => source switch
        {
            DatabaseSourceType.PostgreSQL =>
                """
                DELETE FROM VivInboxMessage
                WHERE (ServiceName, MessageId) IN (
                    SELECT ServiceName, MessageId FROM VivInboxMessage
                    WHERE AcceptedAt < @Cutoff
                    LIMIT @BatchSize
                )
                """,

            _ =>
                """
                DELETE TOP (@BatchSize) FROM VivInboxMessage
                WHERE AcceptedAt < @Cutoff
                """,
        };
    }
}
