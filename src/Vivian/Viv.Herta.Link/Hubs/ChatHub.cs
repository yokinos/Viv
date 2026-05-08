using Microsoft.AspNetCore.SignalR;

namespace Viv.Herta.Link.Hubs
{
    public class ChatHub : Hub
    {
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
                ConnectionPool.Add(Context.ConnectionId, tenantId, userId, appId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            ConnectionPool.Remove(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
