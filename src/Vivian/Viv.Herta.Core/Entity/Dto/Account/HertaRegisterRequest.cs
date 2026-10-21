using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Account
{
    /// <summary>IM 自己注册，不依赖平台身份，所以租户由请求给出</summary>
    public class HertaRegisterRequest : VivApiRequest
    {
        [Required]
        [DisplayName("租户")]
        public long SubjectId { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 3)]
        [DisplayName("登录账号")]
        public string? LoginCode { get; set; }

        [Required]
        [StringLength(64, MinimumLength = 6)]
        [DisplayName("密码")]
        public string? Password { get; set; }

        [StringLength(100)]
        [DisplayName("昵称")]
        public string? NickName { get; set; }
    }
}
