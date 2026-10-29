using System.Data.Common;

using Dapper;

using Viv.Momo;
using Viv.Momo.Dialect;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// Skirk 的全部 SQL —— **独立一份，不共享 Outbox 的能领 SQL，也不塞进 <see cref="ISqlDialect"/>**。
    ///
    /// 表名/列名一律经 <see cref="ISqlDialect.QuoteClr"/> 生成：
    /// SQL Server 得 <c>[TaskType]</c>，PostgreSQL 得 <c>task_type</c>，与库里实际物理名一致。
    ///
    /// 状态列用 <c>int</c>：byte 枚举在 Dapper 里按底层类型拆箱，PG 的 smallint 会抛 Int32 → Byte。
    /// </summary>
    public static class VivAsyncTaskSql
    {
        private const string TableName = "VivAsyncTask";

        private static string T(ISqlDialect d) => d.QuoteClr(TableName);

        /// <summary>按方言取标识符：CLR 名 → 带引号物理名</summary>
        private static string C(ISqlDialect d, string clrName) => d.QuoteClr(clrName);

        #region 建表

        /// <summary>建表 + 建索引（幂等，可重复执行）</summary>
        public static IReadOnlyList<string> CreateTable(ISqlDialect d)
        {
            var table = T(d);
            var bigint = "bigint";
            var intType = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "integer" : "int";
            var text = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "text" : "nvarchar(max)";
            var smallText = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "varchar(64)" : "nvarchar(64)";
            var mediumText = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "varchar(2000)" : "nvarchar(2000)";
            var boolType = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "boolean" : "bit";
            var timeType = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL ? "timestamp" : "datetime2";

            var create = $"""
                CREATE TABLE {table} (
                    {C(d, "Id")} {bigint} NOT NULL,
                    {C(d, "AppId")} {bigint} NULL,
                    {C(d, "SubjectId")} {bigint} NULL,
                    {C(d, "UserId")} {bigint} NULL,
                    {C(d, "TaskType")} {smallText} NOT NULL,
                    {C(d, "ParamsJson")} {text} NOT NULL,
                    {C(d, "Status")} {intType} NOT NULL,
                    {C(d, "Progress")} {intType} NOT NULL,
                    {C(d, "TotalCount")} {intType} NULL,
                    {C(d, "DoneCount")} {intType} NULL,
                    {C(d, "ProgressMessage")} {mediumText} NULL,
                    {C(d, "ResultJson")} {text} NULL,
                    {C(d, "ErrorMessage")} {mediumText} NULL,
                    {C(d, "Attempt")} {intType} NOT NULL,
                    {C(d, "MaxAttempts")} {intType} NOT NULL,
                    {C(d, "LeaseUntil")} {timeType} NULL,
                    {C(d, "LeaseOwner")} {smallText} NULL,
                    {C(d, "NextAttemptAt")} {timeType} NOT NULL,
                    {C(d, "CancelRequested")} {boolType} NOT NULL,
                    {C(d, "IdempotencyKey")} {mediumText} NULL,
                    {C(d, "TraceId")} {smallText} NULL,
                    {C(d, "CreatedAt")} {timeType} NOT NULL,
                    {C(d, "StartedAt")} {timeType} NULL,
                    {C(d, "FinishedAt")} {timeType} NULL,
                    {C(d, "ExpireAt")} {timeType} NULL,
                    CONSTRAINT {C(d, "PK_VivAsyncTask")} PRIMARY KEY ({C(d, "Id")})
                )
                """;

            var createIdempotencyIndex = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL
                ? $"""
                  CREATE UNIQUE INDEX IF NOT EXISTS {C(d, "UX_VivAsyncTask_IdempotencyKey")} ON {table} ({C(d, "IdempotencyKey")}) WHERE {C(d, "IdempotencyKey")} IS NOT NULL
                  """
                : $"""
                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_VivAsyncTask_IdempotencyKey' AND object_id = OBJECT_ID('{TableName}'))
                      CREATE UNIQUE INDEX {C(d, "UX_VivAsyncTask_IdempotencyKey")} ON {table} ({C(d, "IdempotencyKey")}) WHERE {C(d, "IdempotencyKey")} IS NOT NULL
                  """;

            var createClaimIndex = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL
                ? $"""
                  CREATE INDEX IF NOT EXISTS {C(d, "IX_VivAsyncTask_Claim")} ON {table} ({C(d, "Status")}, {C(d, "NextAttemptAt")})
                  """
                : $"""
                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VivAsyncTask_Claim' AND object_id = OBJECT_ID('{TableName}'))
                      CREATE INDEX {C(d, "IX_VivAsyncTask_Claim")} ON {table} ({C(d, "Status")}, {C(d, "NextAttemptAt")})
                  """;

            var createUserIndex = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL
                ? $"""
                  CREATE INDEX IF NOT EXISTS {C(d, "IX_VivAsyncTask_User")} ON {table} ({C(d, "SubjectId")}, {C(d, "UserId")}, {C(d, "CreatedAt")})
                  """
                : $"""
                  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VivAsyncTask_User' AND object_id = OBJECT_ID('{TableName}'))
                      CREATE INDEX {C(d, "IX_VivAsyncTask_User")} ON {table} ({C(d, "SubjectId")}, {C(d, "UserId")}, {C(d, "CreatedAt")})
                  """;

            var body = d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL
                ? create.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS", StringComparison.Ordinal)
                : $"IF OBJECT_ID(N'{TableName}', N'U') IS NULL BEGIN {create} END";

            return [body, createIdempotencyIndex, createClaimIndex, createUserIndex];
        }

        #endregion

        #region 入队 / 查询 / 取消

        public static string Insert(ISqlDialect d)
        {
            var table = T(d);
            return $"""
                INSERT INTO {table} (
                    {C(d, "Id")}, {C(d, "AppId")}, {C(d, "SubjectId")}, {C(d, "UserId")}, {C(d, "TaskType")}, {C(d, "ParamsJson")},
                    {C(d, "Status")}, {C(d, "Progress")}, {C(d, "TotalCount")}, {C(d, "DoneCount")}, {C(d, "ProgressMessage")}, {C(d, "ResultJson")}, {C(d, "ErrorMessage")},
                    {C(d, "Attempt")}, {C(d, "MaxAttempts")}, {C(d, "LeaseUntil")}, {C(d, "LeaseOwner")}, {C(d, "NextAttemptAt")},
                    {C(d, "CancelRequested")}, {C(d, "IdempotencyKey")}, {C(d, "TraceId")},
                    {C(d, "CreatedAt")}, {C(d, "StartedAt")}, {C(d, "FinishedAt")}, {C(d, "ExpireAt")})
                VALUES (
                    @Id, @AppId, @SubjectId, @UserId, @TaskType, @ParamsJson,
                    @Status, @Progress, @TotalCount, @DoneCount, @ProgressMessage, @ResultJson, @ErrorMessage,
                    @Attempt, @MaxAttempts, @LeaseUntil, @LeaseOwner, @NextAttemptAt,
                    @CancelRequested, @IdempotencyKey, @TraceId,
                    @CreatedAt, @StartedAt, @FinishedAt, @ExpireAt)
                """;
        }

        public static string SelectById(ISqlDialect d) => $"SELECT * FROM {T(d)} WHERE {C(d, "Id")} = @Id";

        public static string SelectByIdempotencyKey(ISqlDialect d)
            => $"SELECT {C(d, "Id")} FROM {T(d)} WHERE {C(d, "IdempotencyKey")} = @IdempotencyKey";

        /// <summary>
        /// 列表查询的 WHERE（含 ORDER BY），分页语句由调用方按方言拼。
        /// 抽出来是为了让"列表"和"总数"两条 SQL 的条件永远一致。
        /// </summary>
        private static string QueryWhere(ISqlDialect d) => $"""
            WHERE (@SubjectId IS NULL OR {C(d, "SubjectId")} = @SubjectId)
              AND (@UserId IS NULL OR {C(d, "UserId")} = @UserId)
              AND (@TaskType IS NULL OR {C(d, "TaskType")} = @TaskType)
              AND (@Status IS NULL OR {C(d, "Status")} = @Status)
            ORDER BY {C(d, "CreatedAt")} DESC
            """;

        /// <summary>
        /// 分页取一页。SQL Server 用 <c>OFFSET ... ROWS FETCH NEXT ... ROWS ONLY</c>，
        /// PostgreSQL 用 <c>LIMIT @PageSize OFFSET @Offset</c>。
        /// </summary>
        public static string QueryItems(ISqlDialect d)
        {
            var table = T(d);
            var where = QueryWhere(d);

            return d.Source == Momo.Enums.DatabaseSourceType.PostgreSQL
                ? $"SELECT * FROM {table} {where} LIMIT @PageSize OFFSET @Offset"
                : $"SELECT * FROM {table} {where} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        }

        /// <summary>同条件下的总数（分页用）</summary>
        public static string QueryCount(ISqlDialect d)
        {
            // 去掉 ORDER BY：COUNT 不需要排序，某些库还会因此无法走索引
            var where = QueryWhere(d);
            var orderByIndex = where.LastIndexOf("ORDER BY", StringComparison.Ordinal);
            if (orderByIndex >= 0) where = where[..orderByIndex];

            return $"SELECT COUNT(*) FROM {T(d)} {where}";
        }

        public static string RequestCancel(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET {C(d, "CancelRequested")} = 1
                WHERE {C(d, "Id")} = @Id AND {C(d, "Status")} IN (@Pending, @Running)
                """;

        #endregion

        #region Worker 用（Phase 3 接）

        /// <summary>
        /// 原子认领：挑最多 N 条到期且未被租用的待执行行，置为执行中并写租约。
        ///
        /// SQL Server 没有 SKIP LOCKED，用 READPAST 跳过被别人锁住的行；
        /// PostgreSQL 用 FOR UPDATE SKIP LOCKED。两边都保证多副本不会领到同一条。
        /// </summary>
        public static string ClaimBatch(ISqlDialect d)
        {
            var table = T(d);

            return d.Source switch
            {
                Momo.Enums.DatabaseSourceType.PostgreSQL => $"""
                    UPDATE {table} SET
                        {C(d, "Status")} = @Running,
                        {C(d, "LeaseUntil")} = @LeaseUntil,
                        {C(d, "LeaseOwner")} = @LeaseOwner,
                        {C(d, "StartedAt")} = COALESCE({C(d, "StartedAt")}, @Now),
                        {C(d, "Attempt")} = {C(d, "Attempt")} + 1
                    WHERE {C(d, "Id")} IN (
                        SELECT {C(d, "Id")} FROM {table}
                        WHERE {C(d, "Status")} = @Pending
                          AND {C(d, "NextAttemptAt")} <= @Now
                          AND ({C(d, "LeaseUntil")} IS NULL OR {C(d, "LeaseUntil")} < @Now)
                        ORDER BY {C(d, "NextAttemptAt")}
                        LIMIT @BatchSize
                        FOR UPDATE SKIP LOCKED)
                    RETURNING *
                    """,

                _ => $"""
                    UPDATE {table} WITH (READPAST) SET
                        {C(d, "Status")} = @Running,
                        {C(d, "LeaseUntil")} = @LeaseUntil,
                        {C(d, "LeaseOwner")} = @LeaseOwner,
                        {C(d, "StartedAt")} = COALESCE({C(d, "StartedAt")}, @Now),
                        {C(d, "Attempt")} = {C(d, "Attempt")} + 1
                    OUTPUT INSERTED.*
                    WHERE {C(d, "Id")} IN (
                        SELECT TOP (@BatchSize) {C(d, "Id")} FROM {table} WITH (READPAST)
                        WHERE {C(d, "Status")} = @Pending
                          AND {C(d, "NextAttemptAt")} <= @Now
                          AND ({C(d, "LeaseUntil")} IS NULL OR {C(d, "LeaseUntil")} < @Now)
                        ORDER BY {C(d, "NextAttemptAt")})
                    """
            };
        }

        /// <summary>续租：只有仍持有租约的那一行会被更新（乐观条件防抢）</summary>
        public static string RenewLease(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET {C(d, "LeaseUntil")} = @LeaseUntil
                WHERE {C(d, "Id")} = @Id AND {C(d, "LeaseOwner")} = @LeaseOwner
                """;

        /// <summary>回收过期租约：执行方崩溃后任务回到待执行</summary>
        public static string ReleaseExpiredLeases(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET
                    {C(d, "Status")} = @Pending,
                    {C(d, "LeaseUntil")} = NULL,
                    {C(d, "LeaseOwner")} = NULL,
                    {C(d, "NextAttemptAt")} = @Now
                WHERE {C(d, "Status")} = @Running AND {C(d, "LeaseUntil")} < @Now
                """;

        public static string Complete(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET
                    {C(d, "Status")} = @Succeeded,
                    {C(d, "Progress")} = 100,
                    {C(d, "DoneCount")} = COALESCE({C(d, "TotalCount")}, {C(d, "DoneCount")}),
                    {C(d, "ResultJson")} = @ResultJson,
                    {C(d, "ErrorMessage")} = NULL,
                    {C(d, "LeaseUntil")} = NULL,
                    {C(d, "LeaseOwner")} = NULL,
                    {C(d, "FinishedAt")} = @Now,
                    {C(d, "ExpireAt")} = @ExpireAt
                WHERE {C(d, "Id")} = @Id AND {C(d, "LeaseOwner")} = @LeaseOwner
                """;

        public static string Fail(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET
                    {C(d, "Status")} = @Status,
                    {C(d, "ErrorMessage")} = @ErrorMessage,
                    {C(d, "LeaseUntil")} = NULL,
                    {C(d, "LeaseOwner")} = NULL,
                    {C(d, "NextAttemptAt")} = @NextAttemptAt,
                    {C(d, "FinishedAt")} = CASE WHEN @Status = @Failed THEN @Now ELSE NULL END,
                    {C(d, "ExpireAt")} = CASE WHEN @Status = @Failed THEN @ExpireAt ELSE NULL END
                WHERE {C(d, "Id")} = @Id AND {C(d, "LeaseOwner")} = @LeaseOwner
                """;

        public static string ReportProgress(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET {C(d, "Progress")} = @Progress, {C(d, "TotalCount")} = @TotalCount, {C(d, "DoneCount")} = @DoneCount, {C(d, "ProgressMessage")} = @ProgressMessage
                WHERE {C(d, "Id")} = @Id
                """;

        /// <summary>取消标记（执行方每 500ms 查一次）</summary>
        public static string IsCancelRequested(ISqlDialect d)
            => $"SELECT {C(d, "CancelRequested")} FROM {T(d)} WHERE {C(d, "Id")} = @Id";

        public static string MarkCanceled(ISqlDialect d)
            => $"""
                UPDATE {T(d)} SET
                    {C(d, "Status")} = @Canceled,
                    {C(d, "LeaseUntil")} = NULL,
                    {C(d, "LeaseOwner")} = NULL,
                    {C(d, "FinishedAt")} = @Now,
                    {C(d, "ExpireAt")} = @ExpireAt
                WHERE {C(d, "Id")} = @Id
                """;

        /// <summary>清理：删除保留期已过的终态任务</summary>
        public static string Cleanup(ISqlDialect d)
        {
            var table = T(d);
            return d.Source switch
            {
                Momo.Enums.DatabaseSourceType.PostgreSQL
                    => $"DELETE FROM {table} WHERE {C(d, "Id")} IN (SELECT {C(d, "Id")} FROM {table} WHERE {C(d, "ExpireAt")} < @Now LIMIT @BatchSize)",
                _ => $"""
                    DELETE FROM {table} WHERE {C(d, "Id")} IN (
                        SELECT TOP (@BatchSize) {C(d, "Id")} FROM {table} WHERE {C(d, "ExpireAt")} < @Now)
                    """
            };
        }

        #endregion
    }
}
