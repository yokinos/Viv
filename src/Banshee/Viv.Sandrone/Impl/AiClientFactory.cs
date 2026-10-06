using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;

namespace Viv.Sandrone.Impl
{
    /// <summary>
    /// AI 客户端工厂：把「档位/参数」变成 MEAI 的 <see cref="IChatClient"/>。
    /// 只做构造，不做缓存与密钥解密 —— 那些属于调用方（档位怎么存、密钥怎么加密是各自的事）。
    /// 注册为单例：它只依赖配置，且 Agent 是长生命周期对象、可能在后台线程取用。
    /// </summary>
    public class AiClientFactory : IAiClientFactory
    {
        private readonly OpenAIOptions? _openAIOptions;

        public AiClientFactory(IOptions<OpenAIOptions> options)
        {
            _openAIOptions = options.Value;
        }

        public IChatClient CreateClient(string apiUrl, string apiKey, string model)
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(apiUrl)
            };
            var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

            // 转换为 MEAI 的标准接口
            return openAIClient.GetChatClient(model).AsIChatClient();
        }

        public IChatClient CreateClient(AiModelProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            var client = CreateClient(profile.ApiUrl, profile.ApiKey, profile.Model);

            // 档位里显式给了采样参数才包一层；没给就保持供应商默认，不制造无谓的中间件
            if (profile.Temperature is null && profile.MaxOutputTokens is null) return client;

            return client.AsBuilder()
                .ConfigureOptions(o =>
                {
                    o.Temperature ??= (float?)profile.Temperature;
                    o.MaxOutputTokens ??= profile.MaxOutputTokens;
                })
                .Build();
        }

        [return: MaybeNull]
        public IChatClient? GetDefaultClient()
        {
            // 注意：IOptions<T>.Value 不会是 null —— 节点缺失时拿到的是默认实例（各字段为空）。
            // 所以要判的是"配置是否真的填了"，判 null 是没用的。
            var option = _openAIOptions;
            if (option is null
                || string.IsNullOrWhiteSpace(option.ApiUrl)
                || string.IsNullOrWhiteSpace(option.ApiKey)
                || string.IsNullOrWhiteSpace(option.Model))
            {
                return default;
            }

            return CreateClient(option.ApiUrl, option.ApiKey, option.Model);
        }
    }
}
