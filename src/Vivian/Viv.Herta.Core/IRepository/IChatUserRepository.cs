using Viv.Entity.Database.Herta;

namespace Viv.Herta.Core.IRepository
{
    /// <summary>IM 自有账号。与平台用户表不相通，IM 只认这里的 ChatUserId。</summary>
    public interface IChatUserRepository
    {
        /// <summary>按「归属主体 + 登录账号」定位。域内可能有多主体，账号名不能按域唯一。</summary>
        Task<EtChatUser?> GetByLoginCodeAsync(long ownerSubjectId, string loginCode);

        /// <summary>该归属主体已在用的聊天域；注册时据此复用，避免同一主体散落到多个域。</summary>
        Task<EtChatUser?> GetByOwnerAsync(long ownerSubjectId);

        Task<long> AddAsync(EtChatUser user);

        /// <summary>整行更新。目前只有「登录时口令哈希升级」会用到。</summary>
        Task<bool> UpdateAsync(EtChatUser user);
    }
}
