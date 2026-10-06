using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using Viv.Contracts.Options;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 模型档位提供者：库表 → 缓存 → 解密 → MEAI 客户端。
    /// Agent 工厂从这里取客户端，业务代码不直接碰 OtModelProfile 或密钥。
    /// </summary>
    public interface IModelProfileProvider
    {
        /// <summary>按档位键取档位（已解密）；没有启用的行返回 null</summary>
        Task<AiModelProfile?> GetProfileAsync(string profileKey);

        /// <summary>按档位键取可直接使用的客户端；没有启用的行返回 null</summary>
        Task<IChatClient?> GetChatClientAsync(string profileKey);

        /// <summary>清掉某个档位的缓存（配置改动后调用，不必等 TTL）</summary>
        void Invalidate(string profileKey);
    }
}
