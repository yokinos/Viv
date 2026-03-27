using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    public struct ResultCode
    {
        /// <summary>
        /// 成功
        /// </summary>
        public const int Success = 200;

        /// <summary>
        /// 通用错误
        /// </summary>
        public const int Error = -200;

        /// <summary>
        /// Token错误
        /// </summary>
        public const int TokenInvalid = -400;

        /// <summary>
        /// 身份验证失败/签名错误
        /// </summary>
        public const int AuthError = -401;

        /// <summary>
        /// 无权限
        /// </summary>
        public const int NoPermission = -403;

        /// <summary>
        /// 无权限
        /// </summary>
        public const int NotFound = -404;

        /// <summary>
        /// 系统异常
        /// </summary>
        public const int ServerError = -500;
    }
}
