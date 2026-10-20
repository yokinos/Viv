using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    /// <summary>UserIds 为空表示自己退群</summary>
    public class RemoveGroupMembersRequest : VivApiRequest
    {
        public long GroupId { get; set; }

        public List<long>? UserIds { get; set; }
    }
}
