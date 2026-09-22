-- Inbox 表（SQL Server）
--
-- 幂等：可重复执行。消费端可选：唯一 (ServiceName, IdempotentKey)，与业务写同事务插入。
-- 列名 PascalCase 不带引号，与发件箱同一套口径。
--
-- IdempotentKey 是字符串：消息级去重存 'msg:{MessageId}'，业务级幂等存 'biz:{调用方给的键}'。
-- 两族靠前缀分命名空间，同一条 INSERT 语句配 @IdempotentKey 一个参数。

IF OBJECT_ID(N'VivInboxMessage', N'U') IS NULL
BEGIN
    CREATE TABLE VivInboxMessage (
        ServiceName   NVARCHAR(200) NOT NULL,
        IdempotentKey NVARCHAR(200) NOT NULL,
        AcceptedAt    DATETIME2      NOT NULL,
        CONSTRAINT PK_VivInboxMessage PRIMARY KEY (ServiceName, IdempotentKey)
    );
END

-- 清理按 AcceptedAt 圈行，主键是 (ServiceName, IdempotentKey) 帮不上忙，单独配一个索引。
-- 对已经建过表的库也生效：整份脚本是幂等的，每次启动都会跑一遍。
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_VivInboxMessage_AcceptedAt' AND object_id = OBJECT_ID(N'VivInboxMessage'))
BEGIN
    CREATE INDEX IX_VivInboxMessage_AcceptedAt ON VivInboxMessage (AcceptedAt);
END
