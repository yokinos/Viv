using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Account
{
    public class HertaLoginRequest : VivApiRequest
    {
        [Required]
        [DisplayName("租户")]
        public long SubjectId { get; set; }

        [DisplayName("客户端AppId")]
        public long AppId { get; set; }

        [Required]
        [DisplayName("登录账号")]
        public string? LoginCode { get; set; }

        [Required]
        [DisplayName("登录密码")]
        public string Password { get; set; } = string.Empty;
    }
}
