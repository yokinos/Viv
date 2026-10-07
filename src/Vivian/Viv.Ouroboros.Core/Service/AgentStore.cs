using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
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

        public AgentStore(IMomoDbContext dbContext, ILoggerContract logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        #region 会话

        public async Task<OtConversation?> GetConversationAsync(Guid conversationKey)
            => await _dbContext.SingleOrDefaultAsync<OtConversation>(x => x.ConversationKey == conversationKey);

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
            conversation.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(conversation);
        }

        public async Task<bool> UpdateConversationAsync(OtConversation conversation)
        {
            conversation.UpdatedAt = DateTime.Now;
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
            var rows = await _dbContext.FindListAsync<OtMessage>(x => x.ConversationId == conversationId);
            return rows.Count == 0 ? 1 : rows.Max(x => x.Seq) + 1;
        }

        public async Task<bool> InsertMessageAsync(OtMessage message)
        {
            message.CreatedAt = DateTime.Now;
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
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }

            existing.StateJson = stateJson;
            existing.UpdatedAt = DateTime.Now;
            return await _dbContext.UpdateAsync(existing);
        }

        #endregion

        #region 审批

        public async Task<bool> InsertApprovalAsync(OtApproval approval)
        {
            approval.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(approval);
        }

        public async Task<OtApproval?> GetApprovalAsync(Guid approvalId)
            => await _dbContext.SingleOrDefaultAsync<OtApproval>(x => x.ApprovalId == approvalId);

        public async Task<List<OtApproval>> ListPendingApprovalsAsync(long? subjectId)
        {
            var rows = await _dbContext.FindListAsync<OtApproval>(x => x.SubjectId == subjectId && x.Status == EmApprovalStatus.Pending);
            return rows.OrderBy(x => x.RequestedAt).ToList();
        }

        public async Task<bool> UpdateApprovalAsync(OtApproval approval)
        {
            approval.UpdatedAt = DateTime.Now;
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
            toolCall.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(toolCall);
        }

        public async Task<bool> InsertSubAgentCallAsync(OtSubAgentCall subAgentCall)
        {
            subAgentCall.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(subAgentCall);
        }

        #endregion
    }
}
