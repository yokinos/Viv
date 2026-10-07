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
    /// 模型档位管理实现。密钥单向：入参收明文、加密后写 ApiKeyCipher，出参只有 HasKey ——
    /// 管理后台不需要、也不该拿到可用的密钥原文。
    /// </summary>
    public class ModelProfileAdminService : IModelProfileAdminService, IDependency
    {
        private readonly IMomoDbContext _db;
        private readonly IApiKeyProtector _protector;
        private readonly IConfigChangeNotifier _notifier;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="db">库访问</param>
        /// <param name="protector">密钥加解密</param>
        /// <param name="notifier">配置失效通知</param>
        public ModelProfileAdminService(IMomoDbContext db, IApiKeyProtector protector, IConfigChangeNotifier notifier)
        {
            _db = db;
            _protector = protector;
            _notifier = notifier;
        }

        /// <inheritdoc />
        public async Task<VivApiResult> ListAsync(string? profileKey, int pageIndex, int pageSize)
        {
            var rows = await _db.FindListAsync<OtModelProfile>(x => profileKey == null || x.ProfileKey == profileKey);
            var ordered = rows.OrderBy(x => x.ProfileKey).ThenBy(x => x.Priority).ThenBy(x => x.Id).Select(ToItem).ToList();
            return VivApiResult.Success(AdminPaging.Create(ordered, pageIndex, pageSize));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> GetAsync(long id)
        {
            var row = await _db.FindAsync<OtModelProfile>(id);
            return row is null ? VivApiResult.Failed("模型档位不存在") : VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> CreateAsync(CreateModelProfileRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var invalid = Validate(request.ProfileKey, request.ProviderType, request.ApiUrl, request.Model);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            var row = new OtModelProfile
            {
                Id = IdMagic.NextId(),
                ProfileKey = request.ProfileKey.Trim(),
                ProviderType = (EmProviderType)request.ProviderType,
                ApiUrl = request.ApiUrl.Trim(),
                ApiKeyCipher = EncryptKey(request.ApiKey),
                Model = request.Model.Trim(),
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxOutputTokens,
                TimeoutSeconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : 60,
                Priority = request.Priority ?? 0,
                IsEnabled = request.IsEnabled ?? true,
                Remark = request.Remark,
                CreatedAt = DateTime.Now
            };

            if (!await _db.InsertAsync(row)) return VivApiResult.Failed("写入模型档位失败");

            _notifier.Notify(profileKey: row.ProfileKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> UpdateAsync(UpdateModelProfileRequest request)
        {
            if (request is null) return VivApiResult.Failed("请求体不能为空");

            var row = await _db.FindAsync<OtModelProfile>(request.Id);
            if (row is null) return VivApiResult.Failed("模型档位不存在");

            var invalid = Validate(request.ProfileKey, request.ProviderType, request.ApiUrl, request.Model);
            if (invalid is not null) return VivApiResult.Failed(invalid);

            var oldKey = row.ProfileKey;
            row.ProfileKey = request.ProfileKey.Trim();
            row.ProviderType = (EmProviderType)request.ProviderType;
            row.ApiUrl = request.ApiUrl.Trim();
            row.Model = request.Model.Trim();
            row.Temperature = request.Temperature;
            row.MaxOutputTokens = request.MaxOutputTokens;
            row.TimeoutSeconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : 60;
            row.Priority = request.Priority ?? 0;
            row.IsEnabled = request.IsEnabled ?? row.IsEnabled;
            row.Remark = request.Remark;
            row.UpdatedAt = DateTime.Now;

            // null = 前端没带这一项（不动现有密钥）；空串 = 明确要清掉；非空 = 换新密钥
            if (request.ApiKey is not null) row.ApiKeyCipher = EncryptKey(request.ApiKey);

            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改模型档位失败");

            _notifier.Notify(profileKey: oldKey);
            if (!string.Equals(oldKey, row.ProfileKey, StringComparison.Ordinal)) _notifier.Notify(profileKey: row.ProfileKey);

            return VivApiResult.Success(ToDetail(row));
        }

        /// <inheritdoc />
        public async Task<VivApiResult> SetEnabledAsync(long id, bool isEnabled, bool force)
        {
            var row = await _db.FindAsync<OtModelProfile>(id);
            if (row is null) return VivApiResult.Failed("模型档位不存在");

            if (!isEnabled && !force)
            {
                // 停掉某档位唯一启用的一行 = 用这个档位的 Agent 当场装不出来，先算清有多少个再让人显式确认
                var selfId = row.Id;
                var profileKey = row.ProfileKey;
                var siblings = await _db.FindListAsync<OtModelProfile>(x => x.ProfileKey == profileKey && x.IsEnabled && x.Id != selfId);
                if (siblings.Count == 0)
                {
                    var users = await _db.FindListAsync<OtAgent>(x => x.ModelProfile == profileKey && !x.IsDeleted);
                    if (users.Count > 0)
                        return VivApiResult.Failed($"该行是档位 {profileKey} 唯一启用的一行，还有 {users.Count} 个 Agent 在用；确认要停用请加 force=true");
                }
            }

            row.IsEnabled = isEnabled;
            row.UpdatedAt = DateTime.Now;
            if (!await _db.UpdateAsync(row)) return VivApiResult.Failed("修改模型档位失败");

            _notifier.Notify(profileKey: row.ProfileKey);
            return VivApiResult.Success(ToDetail(row));
        }

        /// <summary>
        /// 明文密钥转密文。空白一律落 NULL：存空串会让运行期走到"解密失败"那条 Error 上，
        /// 而 NULL 的语义是"这一行没配密钥"，两者不该混。
        /// </summary>
        private string? EncryptKey(string? plain) => string.IsNullOrEmpty(plain) ? null : _protector.Encrypt(plain);

        /// <summary>
        /// 共用校验：返回 null 表示通过，否则是给前端的失败原因
        /// </summary>
        private static string? Validate(string? profileKey, int providerType, string? apiUrl, string? model)
        {
            if (string.IsNullOrWhiteSpace(profileKey)) return "profileKey 不能为空";
            if (!Enum.IsDefined(typeof(EmProviderType), providerType)) return $"providerType 非法：{providerType}";
            if (string.IsNullOrWhiteSpace(apiUrl)) return "apiUrl 不能为空";
            if (string.IsNullOrWhiteSpace(model)) return "model 不能为空";
            return null;
        }

        /// <summary>
        /// 实体 → 列表项
        /// </summary>
        private static ModelProfileItemOutput ToItem(OtModelProfile x) => new()
        {
            Id = x.Id,
            ProfileKey = x.ProfileKey,
            ProviderType = (int)x.ProviderType,
            ApiUrl = x.ApiUrl,
            Model = x.Model,
            Priority = x.Priority,
            IsEnabled = x.IsEnabled,
            HasKey = !string.IsNullOrEmpty(x.ApiKeyCipher)
        };

        /// <summary>
        /// 实体 → 详情（绝不回 ApiKeyCipher）
        /// </summary>
        private static ModelProfileDetailOutput ToDetail(OtModelProfile x) => new()
        {
            Id = x.Id,
            ProfileKey = x.ProfileKey,
            ProviderType = (int)x.ProviderType,
            ApiUrl = x.ApiUrl,
            Model = x.Model,
            Temperature = x.Temperature,
            MaxOutputTokens = x.MaxOutputTokens,
            TimeoutSeconds = x.TimeoutSeconds,
            Priority = x.Priority,
            IsEnabled = x.IsEnabled,
            HasKey = !string.IsNullOrEmpty(x.ApiKeyCipher),
            Remark = x.Remark,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };
    }
}
