using Viv.Entity.Database.Herta;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>IM 自有账号。与平台用户表不相通，IM 只认这里的 ChatUserId。</summary>
    public interface IChatUserRepository
    {
        Task<EtChatUser?> GetByLoginCodeAsync(long tenantId, string loginCode);

        Task<long> AddAsync(EtChatUser user);
    }
}
