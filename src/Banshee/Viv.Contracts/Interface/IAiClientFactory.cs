using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Options;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// AI 客户端工厂：把「档位/参数」变成 MEAI 的 <see cref="IChatClient"/>。
    /// 框架侧不再有默认客户端 —— 默认档位不再来自配置文件，改由业务侧按档位传入。
    /// </summary>
    public interface IAiClientFactory
    {
        /// <summary>
        /// 按显式参数建客户端。
        /// </summary>
        IChatClient CreateClient(string apiUrl, string apiKey, string model);

        /// <summary>
        /// 按档位建客户端（多模型入口）。档位来自哪里由调用方决定 ——
        /// 框架只认这一个 POCO，不关心它是数据库读的还是配置读的。
        /// </summary>
        IChatClient CreateClient(AiModelProfile profile);
    }
}
