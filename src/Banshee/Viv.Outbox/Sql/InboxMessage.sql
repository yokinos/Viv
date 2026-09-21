-- Inbox 表（SQL Server）
--
-- 幂等：可重复执行。消费端可选：唯一 (ServiceName, MessageId)，与业务写同事务插入。
-- 列名 PascalCase 不带引号，与发件箱同一套口径。

IF OBJECT_ID(N'VivInboxMessage', N'U') IS NULL
BEGIN
    CREATE TABLE VivInboxMessage (
        ServiceName NVARCHAR(200) NOT NULL,
        MessageId   BIGINT         NOT NULL,
        AcceptedAt  DATETIME2      NOT NULL,
        CONSTRAINT PK_VivInboxMessage PRIMARY KEY (ServiceName, MessageId)
    );
END
