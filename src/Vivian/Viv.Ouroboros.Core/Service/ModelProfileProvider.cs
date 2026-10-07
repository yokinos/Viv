using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.AI;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Log;
using Viv.Ouroboros.Core.IRepository;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 档位提供者实现。
    ///
    /// 缓存策略：档位与客户端一起缓存 60 秒 —— 客户端必须缓存，否则每次请求都新建一个
    /// OpenAI 客户端（各自持有一条 HTTP 管道）；60 秒足够让"改库生效"在体感上是即时的。
    ///
    /// 兜底顺序：同档位多行时取 Priority 最小的一行。运行中调用失败再退到下一行属于调用方的
    /// 重试策略，这里不猜。
    ///
    /// 实现 <see cref="IDependency"/> 走自动注册（类名不以 Service 结尾，DIOption 的后缀扫描扫不到它）。
    /// </summary>
    public class ModelProfileProvider : IModelProfileProvider, IDependency
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);

        private readonly IModelProfileRepository _repository;
        private readonly IApiKeyProtector _protector;
        private readonly IAiClientFactory _clientFactory;
        private readonly IMemoryCacheService _cache;
        private readonly ILoggerContract _logger;

        public ModelProfileProvider(
            IModelProfileRepository repository,
            IApiKeyProtector protector,
            IAiClientFactory clientFactory,
            IMemoryCacheService cache,
            ILoggerContract logger)
        {
            _repository = repository;
            _protector = protector;
            _clientFactory = clientFactory;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>档位与客户端绑在一起缓存，避免两者来自不同版本</summary>
        private sealed record Resolved(AiModelProfile Profile, IChatClient Client);

        public async Task<AiModelProfile?> GetProfileAsync(string profileKey)
            => (await ResolveAsync(profileKey))?.Profile;

        public async Task<IChatClient?> GetChatClientAsync(string profileKey)
            => (await ResolveAsync(profileKey))?.Client;

        public void Invalidate(string profileKey)
        {
            if (string.IsNullOrWhiteSpace(profileKey)) return;
            _cache.Remove($"ouroboros:modelprofile:{profileKey}");
        }

        private async ValueTask<Resolved?> ResolveAsync(string profileKey)
        {
            if (string.IsNullOrWhiteSpace(profileKey)) return null;

            var cacheKey = $"ouroboros:modelprofile:{profileKey}";

            // 只缓存成功解析的结果：null（档位没配/密钥解不开）不进缓存，
            // 否则"刚在库里配好"也要等 TTL 到期才生效（GetOrAddAsync 会把 null 一起缓存）
            if (_cache.TryGet<Resolved>(cacheKey, out var cached) && cached is not null) return cached;

            var resolved = await BuildAsync(profileKey, CancellationToken.None);
            if (resolved is not null) _cache.Set(cacheKey, resolved, CacheTime);

            return resolved;
        }

        private async ValueTask<Resolved?> BuildAsync(string profileKey, CancellationToken token)
        {
            var rows = await _repository.GetEnabledByKeyAsync(profileKey);
            var row = rows.FirstOrDefault();

            if (row is null)
            {
                _logger.Warning("模型档位未配置或未启用：{0}", profileKey);
                return null;
            }

            var apiKey = _protector.Decrypt(row.ApiKeyCipher);
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.Error("模型档位密钥解密失败（内部令牌可能已更换）：{0}", profileKey);
                return null;
            }

            var profile = new AiModelProfile
            {
                ProfileKey = row.ProfileKey,
                ProviderType = (int)row.ProviderType,
                ApiUrl = row.ApiUrl,
                ApiKey = apiKey,
                Model = row.Model,
                Temperature = row.Temperature,
                MaxOutputTokens = row.MaxOutputTokens,
                TimeoutSeconds = row.TimeoutSeconds,
                Priority = row.Priority,
                Remark = row.Remark
            };

            try
            {
                return new Resolved(profile, _clientFactory.CreateClient(profile));
            }
            catch (Exception ex)
            {
                // 地址写错、密钥为空都会在这里炸；记清楚是哪个档位，别让它只在调用时表现为 400/401
                _logger.Error("模型档位构造客户端失败：{0}，{1}", profileKey, ex.Message);
                return null;
            }
        }
    }
}
