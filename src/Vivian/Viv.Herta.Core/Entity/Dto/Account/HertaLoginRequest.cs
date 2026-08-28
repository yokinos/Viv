using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Account
{
    public class HertaLoginRequest : VivApiRequest
    {
        /// <summary>
        /// 机构编码（标识是哪家机构的账号登录的）
        /// </summary>
        [Required]
        public string? TenantCode { get; set; }

        /// <summary>
        /// 登录账号
        /// </summary>
        [Required]
        public string? LoginCode { get; set; }

        /// <summary>
        /// 登录密码
        /// </summary>
        [Required]
        public string Password { get; set; }
    }
}
