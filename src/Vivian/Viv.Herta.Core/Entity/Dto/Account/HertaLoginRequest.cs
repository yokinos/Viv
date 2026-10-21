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

        /// <summary>
        /// 客户端标识。必传：它会被写进 token，连接 Hub 时按这个值判断是不是认识的客户端
        /// </summary>
        [Required]
        [Range(1, long.MaxValue, ErrorMessage = "缺少客户端 AppId")]
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
