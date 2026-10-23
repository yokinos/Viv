using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Herta.Core.Repository
{
    /// <summary>
    /// 消息仓储。单聊与群聊分表，读写都经 <see cref="ChatMessageEntry"/> 收敛。
    /// 读列表走手写 SQL：需要「按 Id 排序 + 只取 N 条」，EF 的谓词查询做不到。
    /// </summary>
    public class ChatMessageRepository : DataAccessBase, IChatMessageRepository
    {
        private readonly IDatabaseOptionsProvider _databaseOptions;

        public ChatMessageRepository(
            IVivContext context, IMomoDbContext dbContext, ILoggerContract logger, IDatabaseOptionsProvider databaseOptions)
            : base(context, dbContext, logger)
        {
            _databaseOptions = databaseOptions;
        }

        public async Task<long> AppendAsync(ChatMessageEntry entry)
        {
            if (entry.ReceiverType == EmChatReceiverType.Group)
            {
                var group = new EtGroupChatMessage
                {
                    TenantId = entry.TenantId,
                    SessionId = entry.SessionId,
                    GroupChatId = entry.GroupId ?? throw new ArgumentException("群聊消息缺少 GroupId", nameof(entry)),
                    UserId = entry.FromUserId,
                    MsgType = (int)entry.MessageType,
                    Content = entry.Body,
                    ReplyMsgId = entry.ReplyMsgId,
                    SendAt = entry.SentAt
                };

                if (!await _dbContext.InsertAsync(group).ConfigureAwait(false))
                    throw new InvalidOperationException("群聊消息写入失败");

                return group.Id;
            }

            var direct = new EtUserChatMessage
            {
                TenantId = entry.TenantId,
                SessionId = entry.SessionId,
                UserId = entry.FromUserId,
                ReceiverUserId = entry.ToUserId ?? throw new ArgumentException("单聊消息缺少 ToUserId", nameof(entry)),
                MsgType = (int)entry.MessageType,
                Content = entry.Body,
                ReplyMsgId = entry.ReplyMsgId,
                SendAt = entry.SentAt
            };

            if (!await _dbContext.InsertAsync(direct).ConfigureAwait(false))
                throw new InvalidOperationException("单聊消息写入失败");

            return direct.Id;
        }

        public async Task<ChatMessageEntry?> GetAsync(long tenantId, long messageId, EmChatReceiverType receiverType)
        {
            if (receiverType == EmChatReceiverType.Group)
            {
                var group = await _dbContext.SingleOrDefaultAsync<EtGroupChatMessage>(
                    x => x.TenantId == tenantId && x.Id == messageId).ConfigureAwait(false);

                return group == null ? null : MapGroup(group);
            }

            var direct = await _dbContext.SingleOrDefaultAsync<EtUserChatMessage>(
                x => x.TenantId == tenantId && x.Id == messageId).ConfigureAwait(false);

            return direct == null ? null : MapDirect(direct);
        }

        public async Task<bool> RecallAsync(long tenantId, long messageId, EmChatReceiverType receiverType, DateTime recalledAt)
        {
            if (receiverType == EmChatReceiverType.Group)
            {
                var group = await _dbContext.SingleOrDefaultAsync<EtGroupChatMessage>(
                    x => x.TenantId == tenantId && x.Id == messageId).ConfigureAwait(false);

                if (group == null) return false;

                group.IsRecall = 1;
                group.RecallAt = recalledAt;
                await _dbContext.UpdateAsync(group).ConfigureAwait(false);
                return true;
            }

            var direct = await _dbContext.SingleOrDefaultAsync<EtUserChatMessage>(
                x => x.TenantId == tenantId && x.Id == messageId).ConfigureAwait(false);

            if (direct == null) return false;

            direct.IsRecall = 1;
            direct.RecallAt = recalledAt;
            await _dbContext.UpdateAsync(direct).ConfigureAwait(false);
            return true;
        }

        public Task<int> CountUnreadAsync(
            long tenantId, EmChatReceiverType receiverType, long userId, long targetId, long lastReadMessageId)
        {
            if (receiverType == EmChatReceiverType.Group)
            {
                // 群聊里自己发的不算未读
                return _dbContext.CountAsync<EtGroupChatMessage>(
                    x => x.TenantId == tenantId
                         && x.GroupChatId == targetId
                         && x.UserId != userId
                         && x.Id > lastReadMessageId);
            }

            return _dbContext.CountAsync<EtUserChatMessage>(
                x => x.TenantId == tenantId
                     && x.UserId == targetId
                     && x.ReceiverUserId == userId
                     && x.Id > lastReadMessageId);
        }

        public async Task<List<ChatMessageEntry>> ListAsync(
            long tenantId, EmChatReceiverType receiverType, long userId, long targetId,
            long afterMessageId, long beforeMessageId, int limit)
        {
            var take = Math.Clamp(limit, 1, 200);

            // after > 0：向后增量，升序取 N
            // 否则（翻历史 / 首屏）：降序取 N 再反转，返回的永远是升序
            var descending = afterMessageId <= 0;
            var isGroup = receiverType == EmChatReceiverType.Group;

            var (sql, parameters) = BuildListSql(
                isGroup, tenantId, userId, targetId, afterMessageId, beforeMessageId, take, descending);

            if (isGroup)
            {
                var groups = await _dbContext.FindListAsync<EtGroupChatMessage>(sql, parameters).ConfigureAwait(false);
                if (descending) groups.Reverse();

                return groups.Select(MapGroup).ToList();
            }

            var directs = await _dbContext.FindListAsync<EtUserChatMessage>(sql, parameters).ConfigureAwait(false);
            if (descending) directs.Reverse();

            return directs.Select(MapDirect).ToList();
        }

        /// <summary>
        /// 批量未读。一页会话逐条 count 就是 N 次查询；这里 JOIN 各自的已读水位，
        /// 两张表各一次。原生 SQL 不过全局过滤器，租户与软删条件自己带。
        /// </summary>
        public async Task<ChatUnreadCounts> CountUnreadManyAsync(
            long tenantId,
            long userId,
            IReadOnlyDictionary<long, long> directWatermarks,
            IReadOnlyDictionary<long, long> groupWatermarks)
        {
            var direct = new Dictionary<long, int>();
            var group = new Dictionary<long, int>();
            if (directWatermarks.Count == 0 && groupWatermarks.Count == 0)
                return new ChatUnreadCounts(direct, group);

            var source = _databaseOptions.GetRealOptions().DatabaseSource;

            string Col(string name) => SqlMagic.QuoteIdentifier(name, source);

            if (directWatermarks.Count > 0)
            {
                // 单聊：发给我的、Id 超过本人会话行已读水位的
                var sql = $"""
                    SELECT m.{Col("UserId")} AS TargetId, COUNT(1) AS Count
                    FROM {SqlMagic.GetTableName<EtUserChatMessage>(source)} m
                    JOIN {SqlMagic.GetTableName<EtChatSession>(source)} s
                      ON s.{Col("TenantId")} = m.{Col("TenantId")}
                     AND s.{Col("UserId")} = @userId
                     AND s.{Col("ChatSessionType")} = 0
                     AND s.{Col("TargetUserId")} = m.{Col("UserId")}
                    WHERE m.{Col("TenantId")} = @tenantId
                      AND m.{Col("ReceiverUserId")} = @userId
                      AND m.{Col("IsDeleted")} = @notDeleted
                      AND m.{Col("Id")} > s.{Col("LastReadMsgId")}
                      AND m.{Col("UserId")} IN @targets
                    GROUP BY m.{Col("UserId")}
                    """;

                var rows = await _dbContext
                    .FindListAsync<UnreadRow>(sql, new { tenantId, userId, notDeleted = false, targets = directWatermarks.Keys })
                    .ConfigureAwait(false);

                foreach (var row in rows) direct[row.TargetId] = row.Count;
            }

            if (groupWatermarks.Count > 0)
            {
                // 群聊：自己发的不算未读
                var sql = $"""
                    SELECT m.{Col("GroupChatId")} AS TargetId, COUNT(1) AS Count
                    FROM {SqlMagic.GetTableName<EtGroupChatMessage>(source)} m
                    JOIN {SqlMagic.GetTableName<EtGroupChatMember>(source)} s
                      ON s.{Col("TenantId")} = m.{Col("TenantId")}
                     AND s.{Col("UserId")} = @userId
                     AND s.{Col("GroupChatId")} = m.{Col("GroupChatId")}
                    WHERE m.{Col("TenantId")} = @tenantId
                      AND m.{Col("UserId")} <> @userId
                      AND m.{Col("IsDeleted")} = @notDeleted
                      AND m.{Col("Id")} > s.{Col("LastReadMsgId")}
                      AND m.{Col("GroupChatId")} IN @targets
                    GROUP BY m.{Col("GroupChatId")}
                    """;

                var rows = await _dbContext
                    .FindListAsync<UnreadRow>(sql, new { tenantId, userId, notDeleted = false, targets = groupWatermarks.Keys })
                    .ConfigureAwait(false);

                foreach (var row in rows) group[row.TargetId] = row.Count;
            }

            return new ChatUnreadCounts(direct, group);
        }

        /// <summary>Dapper 的行映射目标。</summary>
        private sealed class UnreadRow
        {
            public long TargetId { get; set; }

            public int Count { get; set; }
        }

        private static ChatMessageEntry MapGroup(EtGroupChatMessage x) => new()
        {
            Id = x.Id,
            TenantId = x.TenantId,
            SessionId = x.SessionId,
            ReceiverType = EmChatReceiverType.Group,
            FromUserId = x.UserId,
            GroupId = x.GroupChatId,
            MessageType = (EmChatMessageType)x.MsgType,
            Body = x.Content ?? string.Empty,
            ReplyMsgId = x.ReplyMsgId,
            SentAt = x.SendAt,
            IsRecall = x.IsRecall == 1,
            RecallAt = x.RecallAt
        };

        private static ChatMessageEntry MapDirect(EtUserChatMessage x) => new()
        {
            Id = x.Id,
            TenantId = x.TenantId,
            SessionId = x.SessionId,
            ReceiverType = EmChatReceiverType.User,
            FromUserId = x.UserId,
            ToUserId = x.ReceiverUserId,
            MessageType = (EmChatMessageType)x.MsgType,
            Body = x.Content ?? string.Empty,
            ReplyMsgId = x.ReplyMsgId,
            SentAt = x.SendAt,
            IsRecall = x.IsRecall == 1,
            RecallAt = x.RecallAt
        };

        /// <summary>
        /// 拼列表 SQL。表名/列名走 <see cref="SqlMagic"/>（两种方言各自的物理命名），取前 N 条只有 TOP 与 LIMIT 一处差异。
        /// 原生 SQL 不过全局过滤器，租户与软删条件必须自己带。
        /// </summary>
        private (string Sql, object Parameters) BuildListSql(
            bool isGroup, long tenantId, long userId, long targetId,
            long afterMessageId, long beforeMessageId, int take, bool descending)
        {
            var source = _databaseOptions.GetRealOptions().DatabaseSource;

            string Col(string name) => SqlMagic.QuoteIdentifier(name, source);
            string table = isGroup
                ? SqlMagic.GetTableName<EtGroupChatMessage>(source)
                : SqlMagic.GetTableName<EtUserChatMessage>(source);

            var head = source == DatabaseSourceType.SqlServer ? "SELECT TOP (@limit) t.*" : "SELECT t.*";
            var tail = source == DatabaseSourceType.SqlServer ? string.Empty : "LIMIT @limit";
            var direction = descending ? "DESC" : "ASC";

            // 单聊要取两个方向：我发给他的 + 他发给我的
            var scope = isGroup
                ? $"t.{Col("GroupChatId")} = @targetId"
                : $"((t.{Col("UserId")} = @userId AND t.{Col("ReceiverUserId")} = @targetId)"
                  + $" OR (t.{Col("UserId")} = @targetId AND t.{Col("ReceiverUserId")} = @userId))";

            var sql = $"""
                {head} FROM {table} t
                WHERE t.{Col("TenantId")} = @tenantId
                  AND {scope}
                  AND t.{Col("IsDeleted")} = @notDeleted
                  AND (@afterMessageId = 0 OR t.{Col("Id")} > @afterMessageId)
                  AND (@beforeMessageId = 0 OR t.{Col("Id")} < @beforeMessageId)
                ORDER BY t.{Col("Id")} {direction}
                {tail}
                """;

            return (sql, new
            {
                tenantId,
                userId,
                targetId,
                afterMessageId,
                beforeMessageId,
                // bool 而不是 0/1：Postgres 的 boolean 列不能跟整数比
                notDeleted = false,
                limit = take
            });
        }
    }
}
