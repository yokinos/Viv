using System.ComponentModel.DataAnnotations;
using Viv.Delusion.Extension;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Apex.Core.Entity.Dto.Account
{
    public class LoginRequest : VivApiRequest
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

        /// <summary>
        /// [必传] 用户类型
        /// </summary>
        [Required]
        [Display(Name = "用户类型")]
        public EmUserType UserType { get; set; }

        /// <summary>
        /// 一些登录情况下需要设置对应的登陆码
        /// </summary>
        public string? SubjectCode { get; set; }

        public override string Validate()
        {
            if (UserType != EmUserType.Master && SubjectCode.IsNullOrEmpty())
            {
                return "请携带对应的登录Code";
            }

            return base.Validate();
        }
    }
}
