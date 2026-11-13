using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Enums;
using Viv.Ouroboros.Core.IService;

using Viv.Ouroboros.Core.Entity.Model.Agent;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// IAgentStore 实现。全部走 IMomoDbContext，不加缓存 ——
    /// 会话与消息是强一致诉求，缓存留给上层（档位、Agent 装配）。
    /// </summary>
    public class AgentStore : IAgentStore, IDependency
    {
        private readonly IMomoDbContext _dbContext;
        private readonly ILoggerContract _logger;

        /// <summary>取号语句。方言在连接里，实例生命周期内不变，拼一次就够</summary>
        private string? _nextSeqSql;

        public AgentStore(IMomoDbContext dbContext, ILoggerContract logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        #region 会话

        public async Task<OtConversation?> GetConversationAsync(Guid conversationKey)
            => await _dbContext.SingleOrDefaultAsync<OtConversation>(x => x.ConversationKey == conversationKey);

        public async Task<OtConversation?> GetConversationByIdAsync(long conversationId)
            => await _dbContext.SingleOrDefaultAsync<OtConversation>(x => x.Id == conversationId);

        public async Task<List<OtConversation>> ListConversationsAsync(long? subjectId, long? userId, int pageIndex, int pageSize)
        {
            var rows = await _dbContext.FindListAsync<OtConversation>(x => x.SubjectId == subjectId && (userId == null || x.UserId == userId));

            return rows.OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt)
                       .Skip(Math.Max(0, (pageIndex - 1) * pageSize))
                       .Take(pageSize <= 0 ? 20 : pageSize)
                       .ToList();
        }

        public async Task<bool> InsertConversationAsync(OtConversation conversation)
        {
            conversation.CreatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.InsertAsync(conversation);
        }

        public async Task<bool> UpdateConversationAsync(OtConversation conversation)
        {
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.UpdateAsync(conversation);
        }

        #endregion

        #region 消息

        public async Task<List<OtMessage>> ListMessagesAsync(long conversationId, int afterSeq = 0, int limit = 0)
        {
            var rows = await _dbContext.FindListAsync<OtMessage>(x => x.ConversationId == conversationId && x.Seq > afterSeq);
            var ordered = rows.OrderBy(x => x.Seq);

            return limit > 0 ? ordered.Take(limit).ToList() : ordered.ToList();
        }

        public async Task<int> NextSeqAsync(long conversationId)
        {
            // 一次语句里做完「首取时对齐水位」和「自增取号」：
            // UPDATE 会拿到该会话行的排他锁，并发调用在这里排队，所以不会有两个调用读到同一个值。
            // NextSeq = 0 是「还没初始化过」的哨兵，用现有消息的 MAX(Seq) 补上 —— 老库加列后不用跑迁移脚本。
            //
            // 走写连接：语句本身是 UPDATE，读连接可能是只读从库，主从有延迟时还会读到旧水位
            var seq = await _dbContext
                .FindScalarWriteAsync<int?>(_nextSeqSql ??= BuildNextSeqSql(), new { Id = conversationId })
                .ConfigureAwait(false);

            // 0 行受影响 = 会话不存在。这时不能悄悄返回 1：会和已有消息撞号，且错得很安静。
            if (seq is null or <= 0)
            {
                throw new InvalidOperationException($"会话 {conversationId} 不存在，无法分配消息序号");
            }

            return seq.Value;
        }

        /// <summary>
        /// 取号语句。两种方言只有三处不同：NULL 合并函数（ISNULL / COALESCE）、
        /// 返回自增后值的写法（OUTPUT INSERTED / RETURNING）、以及表名列名的引用方式（由方言处理）。
        ///
        /// CASE 分支保证「一条语句同时覆盖初始化和自增」：NextSeq = 0 时按 MAX(Seq) 补水位并直接取 1 号，
        /// 否则单纯 +1。两条语句分开写的话，首次取号和后续取号之间会有窗口。
        /// </summary>
        private string BuildNextSeqSql()
        {
            var dialect = _dbContext.Dialect;
            var conversationTable = dialect.GetTableName<OtConversation>();
            var messageTable = dialect.GetTableName<OtMessage>();

            var id = dialect.QuoteClr(nameof(OtConversation.Id));
            var nextSeq = dialect.QuoteClr(nameof(OtConversation.NextSeq));
            var conversationId = dialect.QuoteClr(nameof(OtMessage.ConversationId));
            var seq = dialect.QuoteClr(nameof(OtMessage.Seq));

            var isPostgres = dialect.Source == DatabaseSourceType.PostgreSQL;
            var coalesce = isPostgres ? "COALESCE" : "ISNULL";

            var set = $"SET {nextSeq} = CASE WHEN {nextSeq} = 0 " +
                      $"THEN (SELECT {coalesce}(MAX({seq}), 0) + 1 FROM {messageTable} WHERE {conversationId} = @Id) " +
                      $"ELSE {nextSeq} + 1 END";

            return isPostgres
                ? $"UPDATE {conversationTable} {set} WHERE {id} = @Id RETURNING {nextSeq}"
                : $"UPDATE {conversationTable} {set} OUTPUT INSERTED.{nextSeq} WHERE {id} = @Id";
        }

        public async Task<bool> InsertMessageAsync(OtMessage message)
        {
            message.CreatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.InsertAsync(message);
        }

        #endregion

        #region 会话状态快照

        public async Task<OtAgentSessionState?> GetSessionStateAsync(long conversationId, string agentKey)
            => await _dbContext.SingleOrDefaultAsync<OtAgentSessionState>(x => x.ConversationId == conversationId && x.AgentKey == agentKey);

        public async Task<bool> SaveSessionStateAsync(long conversationId, string agentKey, string stateJson)
        {
            var existing = await GetSessionStateAsync(conversationId, agentKey);

            if (existing is null)
            {
                return await _dbContext.InsertAsync(new OtAgentSessionState
                {
                    ConversationId = conversationId,
                    AgentKey = agentKey,
                    StateJson = stateJson,
                    SchemaVersion = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }

            existing.StateJson = stateJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.UpdateAsync(existing);
        }

        #endregion

        #region 审批

        public async Task<bool> InsertApprovalAsync(OtApproval approval)
        {
            approval.CreatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.InsertAsync(approval);
        }

        public async Task<OtApproval?> GetApprovalAsync(Guid approvalId)
            => await _dbContext.SingleOrDefaultAsync<OtApproval>(x => x.ApprovalId == approvalId);

        public async Task<List<OtApproval>> ListPendingApprovalsAsync(long? subjectId, long? userId)
        {
            var rows = await _dbContext.FindListAsync<OtApproval>(x => x.SubjectId == subjectId && x.Status == EmApprovalStatus.Pending);
            if (rows.Count == 0) return [];

            // 审批行没有 UserId，归属只能靠会话反查。两次查询而不是逐条查：
            // 待审批通常只有个位数，把涉及的会话一次取回来过滤即可。
            if (userId is not null)
            {
                var conversationIds = rows.Select(x => x.ConversationId).Distinct().ToList();
                var owned = await _dbContext.FindListAsync<OtConversation>(
                    x => conversationIds.Contains(x.Id) && x.UserId == userId);
                var ownedIds = owned.Select(x => x.Id).ToHashSet();

                rows = rows.Where(x => ownedIds.Contains(x.ConversationId)).ToList();
            }

            return rows.OrderBy(x => x.RequestedAt).ToList();
        }

        public async Task<bool> UpdateApprovalAsync(OtApproval approval)
        {
            approval.UpdatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.UpdateAsync(approval);
        }

        #endregion

        #region 工具

        public async Task<List<AgentToolDefinition>> ListEnabledToolsAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return [];

            var bindings = await _dbContext.FindListAsync<OtCapabilityBinding>(
                x => x.AgentKey == agentKey && x.IsEnabled && x.CapabilityType == EmCapabilityType.Tool);

            if (bindings.Count == 0) return [];

            // OtTool 没有软删列（那是 OtAgent 才有的），所以"未删除"在这里只体现为 IsEnabled
            var toolKeys = bindings.Select(x => x.CapabilityKey).Distinct().ToList();
            var tools = await _dbContext.FindListAsync<OtTool>(x => x.IsEnabled && toolKeys.Contains(x.ToolKey));

            var result = new List<AgentToolDefinition>();
            foreach (var binding in bindings.OrderBy(x => x.Priority))
            {
                var tool = tools.FirstOrDefault(x => x.ToolKey == binding.CapabilityKey);
                if (tool is null)
                {
                    // 绑定还在、工具没了或停了：说清楚是哪一条，别让它表现成"模型莫名其妙看不到工具"
                    _logger.Warning("能力绑定指向的工具不存在或未启用，已跳过：{0} → {1}", agentKey, binding.CapabilityKey);
                    continue;
                }

                result.Add(new AgentToolDefinition(binding, tool));
            }

            return result;
        }

        public async Task<List<AgentMcpServerDefinition>> ListEnabledMcpServersAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return [];

            var bindings = await _dbContext.FindListAsync<OtCapabilityBinding>(
                x => x.AgentKey == agentKey && x.IsEnabled && x.CapabilityType == EmCapabilityType.McpServer);

            if (bindings.Count == 0) return [];

            // 与 OtTool 一样，OtMcpServer 也没有软删列，"未删除"同样只体现为 IsEnabled
            var serverNames = bindings.Select(x => x.CapabilityKey).Distinct().ToList();
            var servers = await _dbContext.FindListAsync<OtMcpServer>(x => x.IsEnabled && serverNames.Contains(x.ServerName));

            var result = new List<AgentMcpServerDefinition>();
            foreach (var binding in bindings.OrderBy(x => x.Priority))
            {
                var server = servers.FirstOrDefault(x => x.ServerName == binding.CapabilityKey);
                if (server is null)
                {
                    _logger.Warning("能力绑定指向的 MCP 服务不存在或未启用，已跳过：{0} → {1}", agentKey, binding.CapabilityKey);
                    continue;
                }

                result.Add(new AgentMcpServerDefinition(binding, server));
            }

            return result;
        }

        public async Task<bool> InsertToolCallAsync(OtToolCall toolCall)
        {
            toolCall.CreatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.InsertAsync(toolCall);
        }

        public async Task<bool> InsertSubAgentCallAsync(OtSubAgentCall subAgentCall)
        {
            subAgentCall.CreatedAt = DateTimeOffset.UtcNow;
            return await _dbContext.InsertAsync(subAgentCall);
        }

        #endregion
    }
}
