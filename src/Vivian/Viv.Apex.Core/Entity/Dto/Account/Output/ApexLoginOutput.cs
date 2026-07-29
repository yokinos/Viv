using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Dto.Account.Output
{
    /// <summary>
    /// 账号登录成功返回输出DTO
    /// </summary>
    public class ApexLoginOutput
    {
        /// <summary>
        /// 访问令牌，用于接口鉴权
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// 刷新令牌，AccessToken过期后用于换取新的访问令牌
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// 访问令牌过期时间
        /// </summary>
        public DateTime AccessTokenExpires { get; set; }

        /// <summary>
        /// 刷新令牌过期时间
        /// </summary>
        public DateTime RefreshTokenExpires { get; set; }

        /// <summary>
        /// 用户真实姓名
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        public string? NickName { get; set; }

        /// <summary>
        /// 用户唯一ID
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// 用户手机号
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// 用户头像文件访问地址
        /// </summary>
        public string? AvatarUrl { get; set; }
    }
}