using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Engine.Impl
{
    public class AiClientFactory : IAiClientFactory
    {
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
            var option = VivEngine.VivOptions.OpenAIOption;
            if (option == null) return default;
            return CreateClient(option.ApiUrl, option.ApiKey, option.Model);
        }
    }
}
