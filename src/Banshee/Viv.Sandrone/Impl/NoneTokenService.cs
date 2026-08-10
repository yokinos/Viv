using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;

namespace Viv.Sandrone.Impl
{
    public class NoneTokenService : ITokenService
    {
        public string GenerateToken(TokenPayload payload)
        {
            throw new NotSupportedException("当前环境未启用Token模块，禁止调用鉴权服务");
        }

        public TokenOptions GetOptions()
        {
            throw new NotSupportedException("当前环境未启用Token模块，禁止调用鉴权服务");
        }

        public TokenPayload? ParseToken(string token)
        {
            // 无害降级：无 Token 模块的服务收到带 token 请求时返回 null，而不是抛 500
            return null;
        }

        public bool ValidateToken(string token)
        {
            // 无害降级：无 Token 模块时任何 token 都视为无效，让调用方走未授权分支，而不是抛 500
            return false;
        }
    }
}
