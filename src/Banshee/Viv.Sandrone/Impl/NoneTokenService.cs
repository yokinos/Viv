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

        public TokenPayload ParseToken(string token)
        {
            throw new NotSupportedException("当前环境未启用Token模块，禁止调用鉴权服务");
        }

        public bool ValidateToken(string token)
        {
            throw new NotSupportedException("当前环境未启用Token模块，禁止调用鉴权服务");
        }
    }
}
