using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.Account.Request
{
    public class ApexRefreshRequest : ApiRequestBase
    {
        /// <summary>
        /// 用户Id
        /// </summary>
        [Required]
        [DisplayName("用户Id")]
        public long UserId { get; set; }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        [Required]
        [DisplayName("刷新令牌")]
        public string? RefreshToken { get; set; }
    }
}
