using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Enums;

namespace Viv.Contracts.Options
{
    public class TokenOptions
    {
        /// <summary>
        /// 当前使用的令牌类型
        /// </summary>
        public TokenType TokenType { get; set; } = TokenType.Jwt;

        /// <summary>
        /// 签名/加密密钥（JWT/PASETO通用，建议32位以上）
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Token过期时间（分钟）
        /// </summary>
        public int ExpireMinutes { get; set; } = 120;

        /// <summary>
        /// 发行方
        /// </summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// 受众
        /// </summary>
        public string Audience { get; set; } = string.Empty;
    }
}
