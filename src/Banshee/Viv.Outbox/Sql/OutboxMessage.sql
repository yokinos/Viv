-- 发件箱表（SQL Server）
--
-- 幂等：可重复执行，多实例并发启动安全（投递器启动时自动跑一次，也可交给 DBA / 迁移脚本）。
--
-- 列名一律 PascalCase 且不带引号：SqlServer 不区分大小写，PG 会折叠成小写，
-- 两端写出的 SQL 因此完全一致，手写语句不需要按 provider 分叉列名。
--
-- 时间列统一 UTC（DATETIME2 不带时区概念，写入前一律 DateTime.UtcNow）。

IF OBJECT_ID(N'VivOutboxMessage', N'U') IS NULL
BEGIN
    CREATE TABLE VivOutboxMessage (
        Id             BIGINT         NOT NULL PRIMARY KEY,
        MessageId      BIGINT         NOT NULL,
        EventType      NVARCHAR(500)  NOT NULL,
        Payload        NVARCHAR(MAX)  NOT NULL,
        Status         TINYINT        NOT NULL,
        RetryCount     INT            NOT NULL CONSTRAINT DF_VivOutboxMessage_RetryCount DEFAULT 0,
        NextRetryAt    DATETIME2      NOT NULL,
        LeaseUntil     DATETIME2      NULL,
        OccurredAt     DATETIME2      NOT NULL,
        SentAt         DATETIME2      NULL,
        LastError      NVARCHAR(2000) NULL,
        TraceId        NVARCHAR(64)   NULL,
        RequestTraceId NVARCHAR(200)  NULL
    );

    -- 认领扫描：WHERE Status = 0 AND NextRetryAt <= @Now ORDER BY NextRetryAt, Id
    -- 覆盖列带上 Id，让 TOP (n) 那一支不必回表
    CREATE INDEX IX_VivOutboxMessage_Claim ON VivOutboxMessage (Status, NextRetryAt) INCLUDE (Id);

    -- 排查用：按消费端去重键反查
    CREATE INDEX IX_VivOutboxMessage_MessageId ON VivOutboxMessage (MessageId);
END

-- 溯源两列是后加的，上面那段 IF OBJECT_ID ... IS NULL 对已存在的表整段不进，
-- 不补这两句就永远是 Invalid column name 'TraceId'。
IF COL_LENGTH(N'VivOutboxMessage', N'TraceId') IS NULL
    ALTER TABLE VivOutboxMessage ADD TraceId NVARCHAR(64) NULL;

IF COL_LENGTH(N'VivOutboxMessage', N'RequestTraceId') IS NULL
    ALTER TABLE VivOutboxMessage ADD RequestTraceId NVARCHAR(200) NULL;
