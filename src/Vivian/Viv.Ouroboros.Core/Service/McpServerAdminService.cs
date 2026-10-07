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
    /// MCP 服务管理实现：注册 + 改配置 + 启停。工具的发现与调用在
    /// <see cref="ToolRegistry"/>（装配）与 <see cref="McpClientPool"/>（长连接）里，本类不碰连接。
    /// 任何写操作都会 <see cref="IConfigChangeNotifier.Notify"/> → 版本戳抬高 →
    /// 工具缓存与 MCP 连接池一起失效，改完不必等 60 秒 TTL。
    ///
    /// Headers 只进不出：那一列按注释就是放内部令牌的地方，读接口只回 hasHeaders，
    /// 与模型档位只回 hasKey 同一口径；要改就整体重传，null 表示不动。
    /// </summary>
    public class McpServerAdminService : IMcpServerAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="notifier">配置失效通知</param>
        public McpServerAdminService(IMomoDbContext db, IConfigChangeNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? serverName, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtMcpServer>(x => serverName == null || x.ServerName == serverName);
            var ordered = rows.OrderBy(x => x.ServerName).Select(ToItem).ToList();
            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(long id)
        {
            var row = await _db.FindAsync<OtMcpServer>(id);
            return row is null ? VivApiResult.Failed("MCP 服务不存在") : VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateMcpServerRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");
            if (string.IsNullOrWhiteSpace(request.ServerName)) return VivApiResult.Failed("serverName 不能为空");

            var invalid = Validate(request.Transport, request.ApprovalMode, request.AllowedTools,
                request.Headers, request.AlwaysRequireToolNames, request.NeverRequireToolNames);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            var serverName = request.ServerName.Trim();
            if (await _db.ExistAsync<OtMcpServer>(x => x.ServerName == serverName))
                return VivApiResult.Failed($"serverName 已存在：{serverName}");

            var row = new OtMcpServer
            {
                Id = IdMagic.NextId(),
                ServerName = serverName,
                Transport = (EmMcpTransport)request.Transport,
                ServerAddress = request.ServerAddress,
                ServerDescription = request.ServerDescription,
                AllowedTools = request.AllowedTools,
                Headers = request.Headers,
                ApprovalMode = (EmMcpApprovalMode)(request.ApprovalMode ?? 0),
                AlwaysRequireToolNames = request.AlwaysRequireToolNames,
                NeverRequireToolNames = request.NeverRequireToolNames,
                IsEnabled = request.IsEnabled ?? true,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入 MCP 服务失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateMcpServerRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var row = await _db.FindAsync<OtMcpServer>(request.Id);
            if (row is null) return VivApiResult.Failed("MCP 服务不存在");

            var invalid = Validate(request.Transport, request.ApprovalMode, request.AllowedTools,
                request.Headers, request.AlwaysRequireToolNames, request.NeverRequireToolNames);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            row.Transport = (EmMcpTransport)request.Transport;
            row.ServerAddress = request.ServerAddress;
            row.ServerDescription = request.ServerDescription;
            row.AllowedTools = request.AllowedTools;
            row.ApprovalMode = (EmMcpApprovalMode)(request.ApprovalMode ?? 0);
            row.AlwaysRequireToolNames = request.AlwaysRequireToolNames;
            row.NeverRequireToolNames = request.NeverRequireToolNames;
            row.IsEnabled = request.IsEnabled ?? row.IsEnabled;
            row.UpdatedAt = DateTime.Now;

            // null = 前端没带这一项（不动现有请求头）；空串 = 明确要清掉；非空 = 整体替换
            if (request.Headers is not null) row.Headers = string.IsNullOrWhiteSpace(request.Headers) ? null : request.Headers;

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改 MCP 服务失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force)
        {
            var row = await _db.FindAsync<OtMcpServer>(id);
            if (row is null) return VivApiResult.Failed("MCP 服务不存在");

            if (!isEnabled && !force)
            {
                var serverName = row.ServerName;
                var bindings = await _db.FindListAsync<OtCapabilityBinding>(x => x.CapabilityType == EmCapabilityType.McpServer
                    && x.CapabilityKey == serverName && x.IsEnabled);

                if (bindings.Count > 0)
                    return VivApiResult.Failed($"该服务被 {bindings.Count} 条启用的绑定引用（宿主：{string.Join("、", bindings.Select(x => x.AgentKey).Distinct())}），"
                        + "确认要停用请加 force=true");
            }

            row.IsEnabled = isEnabled;
            row.UpdatedAt = DateTime.Now;
            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改 MCP 服务失败");

            _notifier.Notify();
            return VivApiResult.Success(ToDetail(row));
        }

        /// <summary>
        /// 共用校验：枚举取值与三个 JSON 列的形状。返回 null 表示通过。
        /// </summary>
        private static string? Validate(int transport, int? approvalMode, string? allowedTools,
            string? headers, string? alwaysRequire, string? neverRequire)
        {
            if (!Enum.IsDefined(typeof(EmMcpTransport), transport)) return $"transport 非法：{transport}";
            if (approvalMode is not null && !Enum.IsDefined(typeof(EmMcpApprovalMode), approvalMode.Value))
                return $"approvalMode 非法：{approvalMode}";

            if (!AdminJson.IsJsonObject(headers)) return "headers 不是合法的 JSON 对象";
            if (!AdminJson.IsStringArray(allowedTools)) return "allowedTools 不是合法的字符串数组";
            if (!AdminJson.IsStringArray(alwaysRequire)) return "alwaysRequireToolNames 不是合法的字符串数组";
            if (!AdminJson.IsStringArray(neverRequire)) return "neverRequireToolNames 不是合法的字符串数组";

            return null;
        }

        /// <summary>
        /// 实体 → 列表项（不回 Headers）
        /// </summary>
        private static McpServerItemOutput ToItem(OtMcpServer x) => new()
        {
            Id = x.Id,
            ServerName = x.ServerName,
            Transport = (int)x.Transport,
            ServerAddress = x.ServerAddress,
            ApprovalMode = (int)x.ApprovalMode,
            HasHeaders = !string.IsNullOrWhiteSpace(x.Headers),
            IsEnabled = x.IsEnabled
        };

        /// <summary>
        /// 实体 → 详情（不回 Headers）
        /// </summary>
        private static McpServerDetailOutput ToDetail(OtMcpServer x) => new()
        {
            Id = x.Id,
            ServerName = x.ServerName,
            Transport = (int)x.Transport,
            ServerAddress = x.ServerAddress,
            ServerDescription = x.ServerDescription,
            AllowedTools = x.AllowedTools,
            HasHeaders = !string.IsNullOrWhiteSpace(x.Headers),
            ApprovalMode = (int)x.ApprovalMode,
            AlwaysRequireToolNames = x.AlwaysRequireToolNames,
            NeverRequireToolNames = x.NeverRequireToolNames,
            IsEnabled = x.IsEnabled,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };
    }
}
