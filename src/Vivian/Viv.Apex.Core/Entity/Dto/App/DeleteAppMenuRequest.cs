using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.App
{
    public class DeleteAppMenuRequest : VivApiRequest
    {
        public long ClientAppId { get; set; }

        public long Id { get; set; }
    }
}