using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.Account
{
    public class LoginoutRequest : ApiRequestBase
    {
        [Required]
        [DisplayName("用户Id")]
        public long UserId { get; set; }

        [Required]
        [DisplayName("用户类型")]
        public EmUserType UserType { get; set; }
    }
}
