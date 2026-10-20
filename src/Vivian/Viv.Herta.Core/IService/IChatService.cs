using Viv.Engine;
using Viv.Herta.Core.Entity.Dto.Chat;

namespace Viv.Herta.Core.IService
{
    public interface IChatService
    {
        /// <summary>消息落库与入队在同一事务内</summary>
        Task<VivApiResult> SendMessageAsync(SendMessageRequest request);

        Task<VivApiResult> PullMessagesAsync(PullMessagesRequest request);

        Task<VivApiResult> ListSessionsAsync(int pageIndex, int pageSize);

        /// <summary>已读水位只前进不后退</summary>
        Task<VivApiResult> MarkReadAsync(MarkReadRequest request);

        Task<VivApiResult> CreateGroupAsync(CreateGroupRequest request);

        Task<VivApiResult> AddGroupMembersAsync(AddGroupMembersRequest request);

        Task<VivApiResult> RemoveGroupMembersAsync(RemoveGroupMembersRequest request);

        Task<VivApiResult> ListGroupMembersAsync(long groupId);

        /// <summary>只能撤自己发的，且限时</summary>
        Task<VivApiResult> RecallMessageAsync(RecallMessageRequest request);

        /// <summary>批量查在线态，未知返回 null</summary>
        Task<VivApiResult> GetPresenceAsync(IReadOnlyList<long> userIds);
    }
}
