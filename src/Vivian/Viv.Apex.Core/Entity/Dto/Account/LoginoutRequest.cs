using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.Account
{
    public class LoginoutRequest : VivApiRequest
    {
        [Required]
        [DisplayName("用户类型")]
        public EmUserType UserType { get; set; }
    }
}
