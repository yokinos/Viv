using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    public class CreateGroupRequest : VivApiRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        [DisplayName("群名称")]
        public string? Name { get; set; }

        [StringLength(500)]
        [DisplayName("群头像")]
        public string? AvatarUrl { get; set; }

        /// <summary>初始成员，不含群主（群主取登录身份）</summary>
        [DisplayName("初始成员")]
        public List<long>? MemberUserIds { get; set; }
    }
}
