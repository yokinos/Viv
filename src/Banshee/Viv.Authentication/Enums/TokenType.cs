using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Authentication.Enums
{
    /// <summary>
    /// 令牌类型枚举（支持JWT/PASETO）
    /// </summary>
    public enum TokenType
    {
        /// <summary>
        /// JSON Web Token（默认）
        /// </summary>
        Jwt,

        /// <summary>
        /// Platform-Agnostic Security Tokens（安全升级版JWT）
        /// </summary>
        Paseto
    }
}
