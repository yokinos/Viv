using Viv.Elysia.Request;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    public class AddGroupMembersRequest : VivApiRequest
    {
        public long GroupId { get; set; }

        public List<long>? UserIds { get; set; }
    }
}
