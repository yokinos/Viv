using Microsoft.AspNetCore.SignalR;
using Viv.Herta.Core.IService;

namespace Viv.Herta.Link.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IGroupService _groupService;
        private readonly IConnectionPool _connectionPool;

        public ChatHub(IGroupService groupService, IConnectionPool connectionPool)
        {
            _groupService = groupService;
            _connectionPool = connectionPool;
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            if (httpContext != null
                && httpContext.Request.Query.TryGetValue("tenantId", out var tenantStr)
                && long.TryParse(tenantStr, out var tenantId)
                && httpContext.Request.Query.TryGetValue("userId", out var userIdStr)
                && long.TryParse(userIdStr, out var userId)
                && httpContext.Request.Query.TryGetValue("appId", out var appIdStr)
                && long.TryParse(appIdStr, out var appId))
            {
                _connectionPool.Add(Context.ConnectionId, tenantId, userId, appId);

                var groupIds = await _groupService.GetUserGroupIdsAsync(tenantId, userId);
                foreach (var groupId in groupIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, HertaLinkGroups.GetGroupName(tenantId, groupId));
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connectionPool.Remove(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
