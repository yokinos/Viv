-- 发件箱表（PostgreSQL）
--
-- 幂等：可重复执行，多实例并发启动安全（投递器启动时自动跑一次，也可交给 DBA / 迁移脚本）。
--
-- 列名一律 PascalCase 且不带引号 —— PG 会把未加引号的标识符折叠成小写，
-- 这是**有意的**：同一张表在 SqlServer 上就叫 PascalCase、在 PG 上物理名是小写，
-- 而两边共用的手写 SQL 都不带引号，折叠规则各自生效，结果一致。
--
-- 时间列用 TIMESTAMPTZ（存的是瞬时点，不怕服务器时区不同），
-- 写入前一律 DateTime.UtcNow —— Npgsql 拒绝向 timestamptz 写 Kind=Unspecified 的值。

CREATE TABLE IF NOT EXISTS VivOutboxMessage (
    Id             BIGINT         NOT NULL PRIMARY KEY,
    MessageId      BIGINT         NOT NULL,
    EventType      VARCHAR(500)   NOT NULL,
    Payload        TEXT           NOT NULL,
    Status         SMALLINT       NOT NULL,
    RetryCount     INT            NOT NULL DEFAULT 0,
    NextRetryAt    TIMESTAMPTZ    NOT NULL,
    LeaseUntil     TIMESTAMPTZ    NULL,
    OccurredAt     TIMESTAMPTZ    NOT NULL,
    SentAt         TIMESTAMPTZ    NULL,
    LastError      VARCHAR(2000)  NULL,
    TraceId        VARCHAR(64)    NULL,
    RequestTraceId VARCHAR(200)   NULL
);

-- 认领扫描：WHERE Status = 0 AND NextRetryAt <= @Now ORDER BY NextRetryAt, Id
CREATE INDEX IF NOT EXISTS IX_VivOutboxMessage_Claim ON VivOutboxMessage (Status, NextRetryAt);

-- 排查用：按消费端去重键反查
CREATE INDEX IF NOT EXISTS IX_VivOutboxMessage_MessageId ON VivOutboxMessage (MessageId);

-- 溯源两列是后加的，上面的 CREATE TABLE 对已存在的表整段是 no-op，
-- 不补这两句就永远是 column "traceid" does not exist。
ALTER TABLE VivOutboxMessage ADD COLUMN IF NOT EXISTS TraceId VARCHAR(64) NULL;
ALTER TABLE VivOutboxMessage ADD COLUMN IF NOT EXISTS RequestTraceId VARCHAR(200) NULL;
