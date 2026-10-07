using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Engine;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Momo;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// Agent 管理实现。写入口刻意收紧三处：AgentKey 不可改（绑定/提示词/会话都按它引用）、
    /// 档位与提示词版本必须真实存在（否则写完就装不出来）、停用与删除先算清被谁依赖。
    /// </summary>
    public class AgentAdminService : IAgentAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="notifier">配置失效通知</param>
        public AgentAdminService(IMomoDbContext db, IConfigChangeNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? agentKey, int? agentType, string? ownerDomain, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtAgent>(x => !x.IsDeleted
                && (agentKey == null || x.AgentKey == agentKey)
                && (ownerDomain == null || x.OwnerDomain == ownerDomain));

            // 枚举过滤放内存做：表只有几十行，免得为一次类型判断去踩各 ORM 对 enum 转换的差异
            var filtered = agentType is null ? rows : rows.Where(x => (int)x.AgentType == agentType.Value).ToList();

            var ordered = filtered.OrderBy(x => x.OwnerDomain).ThenBy(x => x.AgentKey).Select(ToItem).ToList();
            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(long id)
        {
            var row = await _db.FindAsync<OtAgent>(id);
            return row is null || row.IsDeleted ? VivApiResult.Failed("Agent 不存在") : VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateAgentRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");
            if (string.IsNullOrWhiteSpace(request.AgentKey)) return VivApiResult.Failed("agentKey 不能为空");
            if (string.IsNullOrWhiteSpace(request.OwnerDomain)) return VivApiResult.Failed("ownerDomain 不能为空");
            if (!Enum.IsDefined(typeof(EmAgentType), request.AgentType)) return VivApiResult.Failed($"agentType 非法：{request.AgentType}");

            var agentKey = request.AgentKey.Trim();
            var exists = await _db.FindListAsync<OtAgent>(x => x.AgentKey == agentKey && !x.IsDeleted);
            if (exists.Count > 0) return VivApiResult.Failed($"agentKey 已存在：{agentKey}");

            var modelProfile = string.IsNullOrWhiteSpace(request.ModelProfile) ? "sub" : request.ModelProfile.Trim();
            var profileInvalid = await CheckProfileAsync(modelProfile);
            if (profileInvalid is not null) return VivApiResult.Failed(profileInvalid);

            var activeVersion = request.ActivePromptVersion ?? 0;
            if (activeVersion > 0)
            {
                var prompts = await _db.FindListAsync<OtAgentPrompt>(x => x.AgentKey == agentKey && x.Version == activeVersion);
                if (prompts.Count == 0) return VivApiResult.Failed($"提示词版本不存在：{agentKey} v{activeVersion}（可以先建 Agent 再补提示词，但生效版本要指向已存在的版本）");
            }

            var row = new OtAgent
            {
                Id = IdMagic.NextId(),
                AgentKey = agentKey,
                AgentType = (EmAgentType)request.AgentType,
                OwnerDomain = request.OwnerDomain.Trim(),
                DisplayName = request.DisplayName,
                Description = request.Description,
                ActivePromptVersion = activeVersion,
                ModelProfile = modelProfile,
                MaxToolIterations = request.MaxToolIterations is > 0 ? request.MaxToolIterations.Value : 8,
                Endpoint = request.Endpoint,
                IsEnabled = request.IsEnabled ?? true,
                Version = 1,
                Remark = request.Remark,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入 Agent 失败");

            _notifier.Notify(agentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateAgentRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var row = await _db.FindAsync<OtAgent>(request.Id);
            if (row is null || row.IsDeleted) return VivApiResult.Failed("Agent 不存在");
            if (string.IsNullOrWhiteSpace(request.OwnerDomain)) return VivApiResult.Failed("ownerDomain 不能为空");
            if (!Enum.IsDefined(typeof(EmAgentType), request.AgentType)) return VivApiResult.Failed($"agentType 非法：{request.AgentType}");
            if (string.IsNullOrWhiteSpace(request.ModelProfile)) return VivApiResult.Failed("modelProfile 不能为空");

            var profileInvalid = await CheckProfileAsync(request.ModelProfile.Trim());
            if (profileInvalid is not null) return VivApiResult.Failed(profileInvalid);

            // null = 不改动生效版本：省得只改个显示名还得把版本号背下来
            if (request.ActivePromptVersion is not null)
            {
                if (request.ActivePromptVersion.Value > 0)
                {
                    var prompts = await _db.FindListAsync<OtAgentPrompt>(
                        x => x.AgentKey == row.AgentKey && x.Version == request.ActivePromptVersion.Value);
                    if (prompts.Count == 0)
                        return VivApiResult.Failed($"提示词版本不存在：{row.AgentKey} v{request.ActivePromptVersion.Value}");
                }

                row.ActivePromptVersion = request.ActivePromptVersion.Value;
            }

            row.AgentType = (EmAgentType)request.AgentType;
            row.OwnerDomain = request.OwnerDomain.Trim();
            row.DisplayName = request.DisplayName;
            row.Description = request.Description;
            row.ModelProfile = request.ModelProfile.Trim();
            row.MaxToolIterations = request.MaxToolIterations is > 0 ? request.MaxToolIterations.Value : 8;
            row.Endpoint = request.Endpoint;
            row.IsEnabled = request.IsEnabled ?? row.IsEnabled;
            row.Remark = request.Remark;
            row.Version++;
            row.UpdatedAt = DateTime.Now;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改 Agent 失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force)
        {
            var row = await _db.FindAsync<OtAgent>(id);
            if (row is null || row.IsDeleted) return VivApiResult.Failed("Agent 不存在");

            if (!isEnabled && !force)
            {
                var reason = await DescribeDependentsAsync(row.AgentKey);
                if (reason is not null)
                    return VivApiResult.Failed($"{reason}；确认要停用请加 force=true");
            }

            row.IsEnabled = isEnabled;
            row.Version++;
            row.UpdatedAt = DateTime.Now;
            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改 Agent 失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> DeleteAsync(long id)
        {
            var row = await _db.FindAsync<OtAgent>(id);
            if (row is null || row.IsDeleted) return VivApiResult.Failed("Agent 不存在");

            // 软删之后所有按 AgentKey 的查询都查不到它，父 Agent 会当场少一个能力，所以先挡住
            var reason = await DescribeDependentsAsync(row.AgentKey);
            if (reason is not null) return VivApiResult.Failed($"不能删除：{reason}");

            row.IsDeleted = true;
            row.IsEnabled = false;
            row.DeletedAt = DateTime.Now;
            row.Version++;
            row.UpdatedAt = DateTime.Now;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("删除 Agent 失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <summary>
        /// 档位必须已启用：Agent 装配第一步就是解析档位，配一个不存在的档位等于建了个装不出来的 Agent
        /// </summary>
        private async Task<string?> CheckProfileAsync(string profileKey)
        {
            var rows = await _db.FindListAsync<OtModelProfile>(x => x.ProfileKey == profileKey && x.IsEnabled);
            return rows.Count > 0 ? null : $"模型档位不存在或未启用：{profileKey}（先建档位，再建 Agent）";
        }

        /// <summary>
        /// 说清这个 Agent 还被谁依赖：被启用的子 Agent 绑定引用，或还有没结束的会话在用它。
        /// 返回 null 表示没人依赖。
        /// </summary>
        private async Task<string?> DescribeDependentsAsync(string agentKey)
        {
            var bindings = await _db.FindListAsync<OtCapabilityBinding>(x => x.CapabilityType == EmCapabilityType.SubAgent
                && x.CapabilityKey == agentKey && x.IsEnabled);
            if (bindings.Count > 0)
                return $"它被 {bindings.Count} 条启用的子 Agent 绑定引用（宿主：{string.Join("、", bindings.Select(x => x.AgentKey).Distinct())}）";

            var conversations = await _db.FindListAsync<OtConversation>(x => x.MainAgentKey == agentKey
                && x.Status != EmConversationStatus.Closed);
            if (conversations.Count > 0)
                return $"还有 {conversations.Count} 个未结束的会话在用这个主 Agent";

            return null;
        }

        /// <summary>
        /// 实体 → 列表项
        /// </summary>
        private static AgentItemOutput ToItem(OtAgent x) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            AgentType = (int)x.AgentType,
            OwnerDomain = x.OwnerDomain,
            DisplayName = x.DisplayName,
            ActivePromptVersion = x.ActivePromptVersion,
            ModelProfile = x.ModelProfile,
            IsEnabled = x.IsEnabled,
            IsDeleted = x.IsDeleted
        };

        /// <summary>
        /// 实体 → 详情
        /// </summary>
        private static AgentDetailOutput ToDetail(OtAgent x) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            AgentType = (int)x.AgentType,
            OwnerDomain = x.OwnerDomain,
            DisplayName = x.DisplayName,
            Description = x.Description,
            ActivePromptVersion = x.ActivePromptVersion,
            ModelProfile = x.ModelProfile,
            MaxToolIterations = x.MaxToolIterations,
            Endpoint = x.Endpoint,
            IsEnabled = x.IsEnabled,
            Version = x.Version,
            Remark = x.Remark,
            IsDeleted = x.IsDeleted,
            DeletedAt = x.DeletedAt,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };
    }
}
