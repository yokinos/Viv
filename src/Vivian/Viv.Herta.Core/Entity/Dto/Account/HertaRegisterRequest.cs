using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Account
{
    /// <summary>
    /// IM 自己注册，不依赖平台身份，所以归属主体由请求给出。
    /// 但「由请求给出」不等于「不校验」：注册时会用 AppId 核对这个主体确实开通了该应用，
    /// 否则任何人都能往任意 SubjectId 下写一条永远登不进去的孤立用户，还会占掉别人的登录账号。
    /// </summary>
    public class HertaRegisterRequest : VivApiRequest
    {
        /// <summary>
        /// 归属主体 Id。
        /// </summary>
        [Required(ErrorMessage = "缺少租户")]
        [Range(1, long.MaxValue, ErrorMessage = "缺少租户")]
        [DisplayName("租户")]
        public long? SubjectId { get; set; }

        /// <summary>
        /// 客户端标识
        /// </summary>
        [Required(ErrorMessage = "缺少客户端 AppId")]
        [Range(1, long.MaxValue, ErrorMessage = "缺少客户端 AppId")]
        [DisplayName("客户端AppId")]
        public long? AppId { get; set; }

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
