-- Inbox 表（PostgreSQL）
--
-- 幂等：CREATE TABLE IF NOT EXISTS。唯一 (ServiceName, IdempotentKey)。
-- 列名不带引号：PG 折叠成小写，Dapper 参数仍按 PascalCase 绑定。
--
-- IdempotentKey 是字符串：消息级去重存 'msg:{MessageId}'，业务级幂等存 'biz:{调用方给的键}'。
-- 两族靠前缀分命名空间，同一条 INSERT 语句配 @IdempotentKey 一个参数。

CREATE TABLE IF NOT EXISTS VivInboxMessage (
    ServiceName   VARCHAR(200) NOT NULL,
    IdempotentKey VARCHAR(200) NOT NULL,
    AcceptedAt    TIMESTAMPTZ  NOT NULL,
    CONSTRAINT PK_VivInboxMessage PRIMARY KEY (ServiceName, IdempotentKey)
);

-- 清理按 AcceptedAt 圈行，主键是 (ServiceName, IdempotentKey) 帮不上忙，单独配一个索引。
CREATE INDEX IF NOT EXISTS IX_VivInboxMessage_AcceptedAt ON VivInboxMessage (AcceptedAt);
