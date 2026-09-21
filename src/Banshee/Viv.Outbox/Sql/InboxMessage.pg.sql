-- Inbox 表（PostgreSQL）
--
-- 幂等：CREATE TABLE IF NOT EXISTS。唯一 (ServiceName, MessageId)。
-- 列名不带引号：PG 折叠成小写，Dapper 参数仍按 PascalCase 绑定。

CREATE TABLE IF NOT EXISTS VivInboxMessage (
    ServiceName VARCHAR(200) NOT NULL,
    MessageId   BIGINT       NOT NULL,
    AcceptedAt  TIMESTAMPTZ  NOT NULL,
    CONSTRAINT PK_VivInboxMessage PRIMARY KEY (ServiceName, MessageId)
);

-- 清理按 AcceptedAt 圈行，主键是 (ServiceName, MessageId) 帮不上忙，单独配一个索引。
CREATE INDEX IF NOT EXISTS IX_VivInboxMessage_AcceptedAt ON VivInboxMessage (AcceptedAt);
