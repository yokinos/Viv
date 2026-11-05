using Viv.Entity.Database.Herta;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>IM 自有账号。与平台用户表不相通，IM 只认这里的 ChatUserId。</summary>
    public interface IChatUserRepository
    {
        Task<EtChatUser?> GetByLoginCodeAsync(long tenantId, string loginCode);

        Task<long> AddAsync(EtChatUser user);

        /// <summary>整行更新。目前只有「登录时口令哈希升级」会用到。</summary>
        Task<bool> UpdateAsync(EtChatUser user);
    }
}
