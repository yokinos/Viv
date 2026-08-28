using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.User
{
    public class GetUserRequest : VivApiRequest
    {
        /// <summary>
        /// 用户Id
        /// </summary>
        [Required]
        [DisplayName("用户Id")]
        public long UserId { get; set; }
    }
}
