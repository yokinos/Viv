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
    /// 能力绑定管理实现。这张表就是权限边界本身，写错了运行期不会报错 ——
    /// 能力键指向不存在的能力、暴露名撞车、子 Agent 成环，全都表现成"模型莫名其妙少一个能力"，
    /// 所以三类问题都在写入口挡住。
    /// </summary>
    public class CapabilityBindingAdminService : ICapabilityBindingAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="notifier">配置失效通知</param>
        public CapabilityBindingAdminService(IMomoDbContext db, IConfigChangeNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? agentKey, int? capabilityType, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtCapabilityBinding>(x => agentKey == null || x.AgentKey == agentKey);

            var filtered = capabilityType is null ? rows : rows.Where(x => (int)x.CapabilityType == capabilityType.Value).ToList();
            var ordered = filtered.OrderBy(x => x.AgentKey).ThenBy(x => x.Priority).ThenBy(x => x.Id).Select(ToItem).ToList();

            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(long id)
        {
            var row = await _db.FindAsync<OtCapabilityBinding>(id);
            return row is null ? VivApiResult.Failed("能力绑定不存在") : VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateCapabilityBindingRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var invalid = await ValidateAsync(request.AgentKey, request.CapabilityType, request.CapabilityKey,
                request.ExposedName, request.AllowedSubjectIds, 0);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            var row = new OtCapabilityBinding
            {
                Id = IdMagic.NextId(),
                AgentKey = request.AgentKey.Trim(),
                CapabilityType = (EmCapabilityType)request.CapabilityType,
                CapabilityKey = request.CapabilityKey.Trim(),
                ExposedName = request.ExposedName,
                ExposedDescription = request.ExposedDescription,
                Priority = request.Priority ?? 0,
                RequiresApproval = request.RequiresApproval,
                AllowedSubjectIds = Normalize(request.AllowedSubjectIds),
                IsEnabled = request.IsEnabled ?? true,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入能力绑定失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateCapabilityBindingRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var row = await _db.FindAsync<OtCapabilityBinding>(request.Id);
            if (row is null) return VivApiResult.Failed("能力绑定不存在");

            // 校验时要把"自己"排掉，否则改个排序都会被自己的暴露名判成重复
            var invalid = await ValidateAsync(row.AgentKey, request.CapabilityType, request.CapabilityKey,
                request.ExposedName, request.AllowedSubjectIds, row.Id);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            row.CapabilityType = (EmCapabilityType)request.CapabilityType;
            row.CapabilityKey = request.CapabilityKey.Trim();
            row.ExposedName = request.ExposedName;
            row.ExposedDescription = request.ExposedDescription;
            row.Priority = request.Priority ?? 0;
            row.RequiresApproval = request.RequiresApproval;
            row.AllowedSubjectIds = Normalize(request.AllowedSubjectIds);
            row.IsEnabled = request.IsEnabled ?? row.IsEnabled;
            row.UpdatedAt = DateTime.Now;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改能力绑定失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled)
        {
            var row = await _db.FindAsync<OtCapabilityBinding>(id);
            if (row is null) return VivApiResult.Failed("能力绑定不存在");

            row.IsEnabled = isEnabled;
            row.UpdatedAt = DateTime.Now;
            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改能力绑定失败");

            _notifier.Notify(row.AgentKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <summary>
        /// 共用校验：宿主、能力键、白名单、暴露名冲突、子 Agent 环。返回 null 表示通过。
        /// </summary>
        /// <param name="agentKey">宿主 Agent 业务键</param>
        /// <param name="capabilityType">能力类型</param>
        /// <param name="capabilityKey">能力键</param>
        /// <param name="exposedName">暴露给模型的名字</param>
        /// <param name="allowedSubjectIds">主体白名单文本</param>
        /// <param name="selfId">正在修改的行 Id；新增传 0</param>
        private async Task<string?> ValidateAsync(string? agentKey, int capabilityType, string? capabilityKey,
            string? exposedName, string? allowedSubjectIds, long selfId)
        {
            if (string.IsNullOrWhiteSpace(agentKey)) return "agentKey 不能为空";
            if (!Enum.IsDefined(typeof(EmCapabilityType), capabilityType)) return $"capabilityType 非法：{capabilityType}";
            if (string.IsNullOrWhiteSpace(capabilityKey)) return "capabilityKey 不能为空";

            var hostKey = agentKey.Trim();
            var key = capabilityKey.Trim();
            var type = (EmCapabilityType)capabilityType;

            var host = await _db.SingleOrDefaultAsync<OtAgent>(x => x.AgentKey == hostKey && !x.IsDeleted);
            if (host is null) return $"宿主 Agent 不存在：{hostKey}";

            // 能力键必须指向真实存在的能力，否则运行期只会把它当脏数据跳过
            switch (type)
            {
                case EmCapabilityType.Tool:
                    if (!await _db.ExistAsync<OtTool>(x => x.ToolKey == key)) return $"工具不存在：{key}（OtTool.ToolKey）";
                    break;

                case EmCapabilityType.SubAgent:
                    if (!await _db.ExistAsync<OtAgent>(x => x.AgentKey == key && !x.IsDeleted)) return $"子 Agent 不存在：{key}（OtAgent.AgentKey）";
                    break;

                case EmCapabilityType.McpServer:
                    if (!await _db.ExistAsync<OtMcpServer>(x => x.ServerName == key)) return $"MCP 服务不存在：{key}（OtMcpServer.ServerName）";
                    break;
            }

            if (!SubjectAllowList.IsValid(allowedSubjectIds))
                return "allowedSubjectIds 必须是主体 Id 的 JSON 数组（如 [1,2]），或留空表示不限制";

            // 只有 Tool / SubAgent 会变成模型可见的工具名，重名会让模型选错工具（MCP 目前不装配）
            if (type is EmCapabilityType.Tool or EmCapabilityType.SubAgent)
            {
                var newName = string.IsNullOrWhiteSpace(exposedName) ? key : exposedName.Trim();
                var peers = await _db.FindListAsync<OtCapabilityBinding>(x => x.AgentKey == hostKey && x.Id != selfId);

                foreach (var peer in peers)
                {
                    if (peer.CapabilityType is not (EmCapabilityType.Tool or EmCapabilityType.SubAgent)) continue;

                    var peerName = string.IsNullOrWhiteSpace(peer.ExposedName) ? peer.CapabilityKey : peer.ExposedName;
                    if (string.Equals(peerName, newName, StringComparison.Ordinal))
                        return $"暴露名重复：{hostKey} 下已有同名能力 {peerName}（绑定 Id={peer.Id}）";
                }
            }

            if (type == EmCapabilityType.SubAgent && await CreatesCycleAsync(hostKey, key))
                return $"子 Agent 绑定会成环：{hostKey} → … → {key}（运行时整条会被跳过）";

            return null;
        }

        /// <summary>
        /// 子 Agent 绑定会不会成环：从待绑的子 Agent 出发，沿"启用的子 Agent 绑定"走，能走回宿主就是环
        /// </summary>
        /// <param name="hostKey">宿主 Agent 业务键</param>
        /// <param name="subKey">待绑的子 Agent 业务键</param>
        private async Task<bool> CreatesCycleAsync(string hostKey, string subKey)
        {
            if (string.Equals(hostKey, subKey, StringComparison.Ordinal)) return true;

            var edges = (await _db.FindListAsync<OtCapabilityBinding>(
                    x => x.CapabilityType == EmCapabilityType.SubAgent && x.IsEnabled))
                .GroupBy(x => x.AgentKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(x => x.CapabilityKey).ToList(), StringComparer.Ordinal);

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(subKey);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current)) continue;
                if (!edges.TryGetValue(current, out var nexts)) continue;

                foreach (var next in nexts)
                {
                    if (string.Equals(next, hostKey, StringComparison.Ordinal)) return true;
                    stack.Push(next);
                }
            }

            return false;
        }

        /// <summary>
        /// 空白白名单统一落 NULL："空数组"与"没配"在 SubjectAllowList 里本来就是同一个语义
        /// </summary>
        private static string? Normalize(string? allowedSubjectIds)
            => string.IsNullOrWhiteSpace(allowedSubjectIds) ? null : allowedSubjectIds.Trim();

        /// <summary>
        /// 实体 → 列表项
        /// </summary>
        private static CapabilityBindingItemOutput ToItem(OtCapabilityBinding x) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            CapabilityType = (int)x.CapabilityType,
            CapabilityKey = x.CapabilityKey,
            ExposedName = x.ExposedName,
            Priority = x.Priority,
            RequiresApproval = x.RequiresApproval,
            IsEnabled = x.IsEnabled
        };

        /// <summary>
        /// 实体 → 详情
        /// </summary>
        private static CapabilityBindingDetailOutput ToDetail(OtCapabilityBinding x) => new()
        {
            Id = x.Id,
            AgentKey = x.AgentKey,
            CapabilityType = (int)x.CapabilityType,
            CapabilityKey = x.CapabilityKey,
            ExposedName = x.ExposedName,
            ExposedDescription = x.ExposedDescription,
            Priority = x.Priority,
            RequiresApproval = x.RequiresApproval,
            AllowedSubjectIds = x.AllowedSubjectIds,
            IsEnabled = x.IsEnabled,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };
    }
}
