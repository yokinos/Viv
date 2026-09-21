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
    }
}
