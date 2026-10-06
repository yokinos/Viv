using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Dto;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>会话管理：创建、列表、详情、消息、结束。返回统一信封，控制器只转发。</summary>
    public interface IConversationService
    {
        Task<VivApiResult> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default);

        Task<VivApiResult> ListAsync(int pageIndex = 1, int pageSize = 20);

        Task<VivApiResult> GetAsync(Guid conversationKey, int lastMessageCount = 50);

        Task<VivApiResult> ListMessagesAsync(Guid conversationKey, int afterSeq = 0, int limit = 200);

        Task<VivApiResult> CloseAsync(Guid conversationKey);
    }
}
