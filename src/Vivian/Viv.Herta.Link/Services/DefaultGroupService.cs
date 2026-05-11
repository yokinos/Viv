using Viv.Herta.Core.IService;

namespace Viv.Herta.Link.Services
{
    public class DefaultGroupService : IGroupService
    {
        public Task<List<long>> GetUserGroupIdsAsync(long tenantId, long userId)
        {
            return Task.FromResult(new List<long>());
        }
    }
}
