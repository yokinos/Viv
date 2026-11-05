using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Account
{
    public class HertaLoginRequest : VivApiRequest
    {
        /// <summary>归属主体 Id。可空 + Range：只靠 <c>[Required]</c> 挡不住值类型的 0（详见 HertaRegisterRequest）</summary>
        [Required(ErrorMessage = "缺少租户")]
        [Range(1, long.MaxValue, ErrorMessage = "缺少租户")]
        [DisplayName("租户")]
        public long? SubjectId { get; set; }

        /// <summary>
        /// 客户端标识。必传：它会被写进 token，连接 Hub 时按这个值判断是不是认识的客户端
        /// </summary>
        [Required(ErrorMessage = "缺少客户端 AppId")]
        [Range(1, long.MaxValue, ErrorMessage = "缺少客户端 AppId")]
        [DisplayName("客户端AppId")]
        public long? AppId { get; set; }

        [Required]
        [DisplayName("登录账号")]
        public string? LoginCode { get; set; }

        [Required]
        [DisplayName("登录密码")]
        public string Password { get; set; } = string.Empty;
    }
}
