using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Entity.Request;

namespace Viv.Apex.Core.Entity.Dto.Account.Request
{
    public class ApexLoginRequest : RequestBase
    {
        /// <summary>
        /// 账户名
        /// </summary>
        [Required]
        [Display(Name = "账户名")]
        public string? UserName { get; set; }

        /// <summary>
        /// [必传]密码
        /// </summary>
        [Required]
        [Display(Name = "密码")]
        public string? Password { get; set; }
    }
}
