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
    /// 工具管理实现。ParamsSchema 在写入口就要是合法 JSON 对象 —— 运行期那份实现只记一句 Warning
    /// 然后退回"按委托推断"，坏 schema 会表现成"模型看到的入参和库里写的不一样"，很难查。
    /// </summary>
    public class ToolAdminService : IToolAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="notifier">配置失效通知</param>
        public ToolAdminService(IMomoDbContext db, IConfigChangeNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? toolKey, string? ownerDomain, int? transport, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtTool>(x =>
                (toolKey == null || x.ToolKey == toolKey) && (ownerDomain == null || x.OwnerDomain == ownerDomain));

            var filtered = transport is null ? rows : rows.Where(x => (int)x.Transport == transport.Value).ToList();
            var ordered = filtered.OrderBy(x => x.OwnerDomain).ThenBy(x => x.ToolKey).Select(ToItem).ToList();

            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(long id)
        {
            var row = await _db.FindAsync<OtTool>(id);
            return row is null ? VivApiResult.Failed("工具不存在") : VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateToolRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var invalid = Validate(request.OwnerDomain, request.ParamsSchema, request.Transport, request.Endpoint);
            if (invalid is not null) return VivApiResult.Failed(invalid);
            if (string.IsNullOrWhiteSpace(request.ToolKey)) return VivApiResult.Failed("toolKey 不能为空");

            var toolKey = request.ToolKey.Trim();
            var exists = await _db.FindListAsync<OtTool>(x => x.ToolKey == toolKey);
            if (exists.Count > 0) return VivApiResult.Failed($"toolKey 已存在：{toolKey}");

            var row = new OtTool
            {
                Id = IdMagic.NextId(),
                ToolKey = toolKey,
                OwnerDomain = request.OwnerDomain.Trim(),
                Description = request.Description,
                ParamsSchema = request.ParamsSchema,
                Transport = (EmToolTransport)request.Transport,
                Endpoint = request.Endpoint,
                RequiresApproval = request.RequiresApproval ?? false,
                IsReadOnly = request.IsReadOnly ?? true,
                IsEnabled = request.IsEnabled ?? true,
                Remark = request.Remark,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入工具失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateToolRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var row = await _db.FindAsync<OtTool>(request.Id);
            if (row is null) return VivApiResult.Failed("工具不存在");

            var invalid = Validate(request.OwnerDomain, request.ParamsSchema, request.Transport, request.Endpoint);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            row.OwnerDomain = request.OwnerDomain.Trim();
            row.Description = request.Description;
            row.ParamsSchema = request.ParamsSchema;
            row.Transport = (EmToolTransport)request.Transport;
            row.Endpoint = request.Endpoint;
            row.RequiresApproval = request.RequiresApproval ?? false;
            row.IsReadOnly = request.IsReadOnly ?? true;
            row.IsEnabled = request.IsEnabled ?? row.IsEnabled;
            row.Remark = request.Remark;
            row.UpdatedAt = DateTime.Now;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改工具失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force)
        {
            var row = await _db.FindAsync<OtTool>(id);
            if (row is null) return VivApiResult.Failed("工具不存在");

            if (!isEnabled && !force)
            {
                // 工具没有软删列，"停用"就是唯一的急停开关；但仍要先说清有多少条绑定会跟着失效
                var toolKey = row.ToolKey;
                var bindings = await _db.FindListAsync<OtCapabilityBinding>(x => x.CapabilityType == EmCapabilityType.Tool
                    && x.CapabilityKey == toolKey && x.IsEnabled);

                if (bindings.Count > 0)
                    return VivApiResult.Failed($"该工具被 {bindings.Count} 条启用的绑定引用（宿主：{string.Join("、", bindings.Select(x => x.AgentKey).Distinct())}），"
                        + "停用后这些 Agent 会静默少一个工具；确认要停用请加 force=true");
            }

            row.IsEnabled = isEnabled;
            row.UpdatedAt = DateTime.Now;
            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改工具失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <summary>
        /// 共用校验：返回 null 表示通过
        /// </summary>
        private static string? Validate(string? ownerDomain, string? paramsSchema, int transport, string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(ownerDomain)) return "ownerDomain 不能为空";
            if (!Enum.IsDefined(typeof(EmToolTransport), transport)) return $"transport 非法：{transport}";

            if (!AdminJson.IsJsonObject(paramsSchema))
                return "paramsSchema 不是合法的 JSON 对象，已拒绝入库；请给出 JSON Schema，或留空";

            // Http 工具没有地址，装配时只会被记一句 Warning 后跳过 —— 那种失败在运行期是看不见的
            if ((EmToolTransport)transport == EmToolTransport.Http && string.IsNullOrWhiteSpace(endpoint))
                return "transport=Http 时 endpoint 不能为空";

            return null;
        }

        /// <summary>
        /// 实体 → 列表项
        /// </summary>
        private static ToolItemOutput ToItem(OtTool x) => new()
        {
            Id = x.Id,
            ToolKey = x.ToolKey,
            OwnerDomain = x.OwnerDomain,
            Transport = (int)x.Transport,
            Endpoint = x.Endpoint,
            RequiresApproval = x.RequiresApproval,
            IsReadOnly = x.IsReadOnly,
            IsEnabled = x.IsEnabled
        };

        /// <summary>
        /// 实体 → 详情
        /// </summary>
        private static ToolDetailOutput ToDetail(OtTool x) => new()
        {
            Id = x.Id,
            ToolKey = x.ToolKey,
            OwnerDomain = x.OwnerDomain,
            Description = x.Description,
            ParamsSchema = x.ParamsSchema,
            Transport = (int)x.Transport,
            Endpoint = x.Endpoint,
            RequiresApproval = x.RequiresApproval,
            IsReadOnly = x.IsReadOnly,
            IsEnabled = x.IsEnabled,
            Remark = x.Remark,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };
    }
}
