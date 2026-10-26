using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.Account
{
    public class RefreshRequest : VivApiRequest
    {
        /// <summary>
        /// 客户端对外标识（对应 AtClientApp.AppKey），不是主键 Id
        /// </summary>
        [Required]
        [StringLength(64)]
        [DisplayName("客户端AppKey")]
        public string? AppKey { get; set; }

        [Required]
        [DisplayName("用户Id")]
        public long UserId { get; set; }

        [Required]
        [DisplayName("用户类型")]
        public EmUserType UserType { get; set; }

        [Required]
        [DisplayName("刷新令牌")]
        public string? RefreshToken { get; set; }
    }
}
