using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Engine;
using Viv.Entity.Database.Ouroboros;
using Viv.Momo;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 提示词管理实现。
    ///
    /// 新增版本**不**顺带改 OtAgent.ActivePromptVersion：版本化的全部意义就是"写完不等于上线"，
    /// 顺手切等于把每次存草稿都变成一次生产发布，也吃掉了出问题时"切回上一版"的对照点。
    /// 想让它生效，调 activate 显式切 —— 一次调用，仍然即时生效。
    /// </summary>
    public class AgentPromptAdminService : IAgentPromptAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="notifier">配置失效通知</param>
        public AgentPromptAdminService(IMomoDbContext db, IConfigChangeNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? agentKey, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtAgentPrompt>(x => agentKey == null || x.AgentKey == agentKey);
            var actives = await LoadActiveVersionsAsync();

            var ordered = rows.OrderBy(x => x.AgentKey).ThenBy(x => x.Version)
                .Select(x => ToItem(x, ActiveOf(actives, x.AgentKey))).ToList();

            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(string agentKey, int version)
        {
            var row = await _db.SingleOrDefaultAsync<OtAgentPrompt>(x => x.AgentKey == agentKey && x.Version == version);
            if (row is null) return VivApiResult.Failed($"提示词不存在：{agentKey} v{version}");

            var actives = await LoadActiveVersionsAsync();
            return VivApiResult.Success(ToDetail(row, ActiveOf(actives, row.AgentKey)));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateAgentPromptRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");
            if (string.IsNullOrWhiteSpace(request.AgentKey)) return VivApiResult.Failed("agentKey 不能为空");
            if (string.IsNullOrWhiteSpace(request.Content)) return VivApiResult.Failed("content 不能为空");

            var agentKey = request.AgentKey.Trim();
            var agent = await _db.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == agentKey && !x.IsDeleted);
            if (agent is null) return VivApiResult.Failed($"Agent 不存在：{agentKey}");

            var rows = await _db.FindListAsync<OtAgentPrompt>(x => x.AgentKey == agentKey);
            var version = request.Version is > 0 ? request.Version.Value : (rows.Count == 0 ? 1 : rows.Max(x => x.Version) + 1);

            // 版本号是历史坐标，重复就直接报错而不是覆盖：覆盖等于悄悄抹掉一个回滚点
            if (rows.Exists(x => x.Version == version)) return VivApiResult.Failed($"该版本已存在：{agentKey} v{version}");

            var row = new OtAgentPrompt
            {
                Id = IdMagic.NextId(),
                AgentKey = agentKey,
                Version = version,
                Content = request.Content,
                Notes = request.Notes,
                Remark = request.Remark,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入提示词失败");

            _notifier.Notify(agentKey);
            return VivApiResult.Success(ToDetail(row, agent.ActivePromptVersion));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateAgentPromptRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");
            if (string.IsNullOrWhiteSpace(request.Content)) return VivApiResult.Failed("content 不能为空");

            var row = await _db.FindAsync<OtAgentPrompt>(request.Id);
            if (row is null) return VivApiResult.Failed("提示词不存在");

            var agent = await _db.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == row.AgentKey && !x.IsDeleted);
            if (agent is not null && agent.ActivePromptVersion == row.Version)
                return VivApiResult.Failed($"v{row.Version} 是 {row.AgentKey} 当前生效的版本，不能直接改；请新增一版再 activate 切换");

            row.Content = request.Content;
            row.Notes = request.Notes;
            row.Remark = request.Remark;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改提示词失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row, agent?.ActivePromptVersion ?? 0));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ActivateAsync(ActivateAgentPromptRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");
            if (string.IsNullOrWhiteSpace(request.AgentKey)) return VivApiResult.Failed("agentKey 不能为空");

            var agentKey = request.AgentKey.Trim();
            var version = request.Version;

            var agent = await _db.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == agentKey && !x.IsDeleted);
            if (agent is null) return VivApiResult.Failed($"Agent 不存在：{agentKey}");

            var prompts = await _db.FindListAsync<OtAgentPrompt>(x => x.AgentKey == agentKey && x.Version == version);
            if (prompts.Count == 0) return VivApiResult.Failed($"提示词版本不存在：{agentKey} v{version}");

            agent.ActivePromptVersion = version;
            agent.Version++;
            agent.UpdatedAt = DateTime.Now;

            if (!await _db.UpdateAsync(agent)) return VivApiResult.Failed("切换提示词版本失败");

            _notifier.Notify(agentKey);
            return VivApiResult.Success(ToDetail(prompts[0], version));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> DeleteAsync(long id)
        {
            var row = await _db.FindAsync<OtAgentPrompt>(id);
            if (row is null) return VivApiResult.Failed("提示词不存在");

            var agent = await _db.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == row.AgentKey && !x.IsDeleted);
            if (agent is not null && agent.ActivePromptVersion == row.Version)
                return VivApiResult.Failed($"v{row.Version} 是 {row.AgentKey} 当前生效的版本，删了 Agent 就装不出来；请先 activate 到别的版本");

            // OtAgentPrompt 没有软删列，只能真删：硬删前把话说死，别让"删错了"变成只能靠数据库备份
            if (!await _db.DeleteAsync<OtAgentPrompt>(id)) return VivApiResult.Failed("删除提示词失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success();
        }

        /// <summary>
        /// 取各 Agent 当前生效的提示词版本（用来在列表里标 IsActive）。
        /// 表小，整表读一次比按 AgentKey 逐个查省事。
        /// </summary>
        private async Task<Dictionary<string, int>> LoadActiveVersionsAsync()
        {
            var agents = await _db.FindListAsync<OtAgent>(x => !x.IsDeleted);

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var agent in agents)
            {
                if (!result.ContainsKey(agent.AgentKey)) result[agent.AgentKey] = agent.ActivePromptVersion;
            }

            return result;
        }

        /// <summary>
        /// 该 Agent 的生效版本；Agent 已不存在时返回 -1（列表里一律标成非生效）
        /// </summary>
        private static int ActiveOf(Dictionary<string, int> actives, string agentKey)
            => actives.TryGetValue(agentKey, out var version) ? version : -1;

        /// <summary>
        /// 实体 → 列表项（不回正文）
        /// </summary>
        private static AgentPromptItemOutput ToItem(OtAgentPrompt x, int activeVersion) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            Version = x.Version,
            IsActive = x.Version == activeVersion,
            ContentLength = x.Content?.Length ?? 0,
            Remark = x.Remark,
            CreatedAt = x.CreatedAt
        };

        /// <summary>
        /// 实体 → 详情
        /// </summary>
        private static AgentPromptDetailOutput ToDetail(OtAgentPrompt x, int activeVersion) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            Version = x.Version,
            IsActive = x.Version == activeVersion,
            Content = x.Content,
            Notes = x.Notes,
            Remark = x.Remark,
            CreatedAt = x.CreatedAt
        };
    }
}
