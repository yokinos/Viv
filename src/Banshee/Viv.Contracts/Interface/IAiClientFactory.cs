using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Options;

namespace Viv.Contracts.Interface
{
    public interface IAiClientFactory
    {
        /// <summary>
        /// 取默认（appsettings 里 OpenAIOption 配的那一个）客户端；未配置返回 null。
        /// </summary>
        IChatClient? GetDefaultClient();

        /// <summary>
        /// 按显式参数建客户端。
        /// </summary>
        IChatClient CreateClient(string apiUrl, string apiKey, string model);

        IChatClient CreateClient(OpenAIOptions option) => CreateClient(option.ApiUrl, option.ApiKey, option.Model);

        /// <summary>
        /// 按档位建客户端（多模型入口）。档位来自哪里由调用方决定 ——
        /// 框架只认这一个 POCO，不关心它是数据库读的还是配置读的。
        /// </summary>
        IChatClient CreateClient(AiModelProfile profile);
    }
}
