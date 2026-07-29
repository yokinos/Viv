using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.Account.Request
{
    public class ApexLoginRequest : ApiRequestBase
    {
        /// <summary>
        /// 账户名
        /// </summary>
        [Required]
        [StringLength(20)]
        [Display(Name = "账户名")]
        public string? UserName { get; set; }

        /// <summary>
        /// [必传]密码
        /// </summary>
        [Required]
        [StringLength(20)]
        [Display(Name = "密码")]
        public string? Password { get; set; }
    }
}
