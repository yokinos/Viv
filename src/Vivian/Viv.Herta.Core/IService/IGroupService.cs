namespace Viv.Herta.Core.IService
{
    public interface IGroupService
    {
        Task<List<long>> GetUserGroupIdsAsync(long tenantId, long userId);
    }
}
