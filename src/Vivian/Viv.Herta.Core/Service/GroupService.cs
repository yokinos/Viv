using Viv.Herta.Core.IRepository;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Core.Service
{
    public class GroupService : IGroupService
    {
        private readonly IChatGroupMemberRepository _repository;

        public GroupService(IChatGroupMemberRepository repository)
        {
            _repository = repository;
        }

        public Task<List<long>> GetUserGroupIdsAsync(long tenantId, long userId)
            => _repository.GetGroupIdsAsync(tenantId, userId);
    }
}
