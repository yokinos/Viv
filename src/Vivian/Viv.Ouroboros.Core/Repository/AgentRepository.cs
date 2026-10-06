using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Entity.Database.Ouroboros;
using Viv.Momo;
using Viv.Ouroboros.Core.IRepository;

namespace Viv.Ouroboros.Core.Repository
{
    /// <summary>
    /// Agent 定义仓储。纯库表读写。
    /// </summary>
    public class AgentRepository : IAgentRepository
    {
        private readonly IMomoDbContext _dbContext;

        public AgentRepository(IMomoDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<OtAgent?> GetByKeyAsync(string agentKey)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return null;
            return await _dbContext.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == agentKey && !x.IsDeleted);
        }

        public async Task<string?> GetPromptContentAsync(string agentKey, int version)
        {
            var prompt = await _dbContext.SingleOrDefaultAsync<OtAgentPrompt>(x => x.AgentKey == agentKey && x.Version == version);
            return prompt?.Content;
        }

        public async Task<List<OtCapabilityBinding>> GetCapabilitiesAsync(string agentKey)
        {
            var rows = await _dbContext.FindListAsync<OtCapabilityBinding>(x => x.AgentKey == agentKey && x.IsEnabled);
            return rows.OrderBy(x => x.Priority).ToList();
        }
    }
}
