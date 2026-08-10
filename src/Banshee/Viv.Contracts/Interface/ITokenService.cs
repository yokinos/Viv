using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Models;
using Viv.Contracts.Options;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 令牌服务抽象接口（统一JWT/PASETO操作）
    /// </summary>
    public interface ITokenService
    {
        TokenOptions GetOptions();

        /// <summary>
        /// 生成令牌
        /// </summary>
        /// <param name="payload">令牌载荷</param>
        /// <returns>生成的Token字符串</returns>
        string GenerateToken(TokenPayload payload);

        /// <summary>
        /// 验证令牌有效性
        /// </summary>
        /// <param name="token">令牌字符串</param>
        /// <returns>是否有效</returns>
        bool ValidateToken(string token);

        /// <summary>
        /// 解析令牌载荷
        /// </summary>
        /// <param name="token">令牌字符串</param>
        /// <returns>解析后的载荷模型</returns>
        /// <exception cref="Exceptions.InvalidTokenException">令牌无效时抛出</exception>
        TokenPayload? ParseToken(string token);
    }
}
