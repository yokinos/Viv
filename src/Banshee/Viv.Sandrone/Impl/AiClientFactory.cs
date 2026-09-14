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
    public class AiClientFactory : IAiClientFactory
    {
        private readonly OpenAIOptions _openAIOptions;

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

        [return: MaybeNull]
        public IChatClient GetDefaultClient()
        {
            if (_openAIOptions == null) return default;
            return CreateClient(_openAIOptions.ApiUrl, _openAIOptions.ApiKey, _openAIOptions.Model);
        }
    }
}
