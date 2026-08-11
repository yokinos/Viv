using Microsoft.AspNetCore.SignalR;
using Viv.Engine.Power;
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

            // 身份只来自网关认证后回填的 x-viv-* 头（由 RequestTokenResolver 验签 x-request-token）。
            // 客户端 query 串直传的 tenantId/userId/appId 已被网关剥离，这里也一律不读——无认证即可冒充任意用户/租户的漏洞点。
            var identity = httpContext == null ? null : RequestTokenResolver.GetContextFromHeaders(httpContext);
            if (identity == null || identity.AppId <= 0 || identity.SubjectId <= 0 || identity.UserId <= 0)
            {
                Context.Abort();
                return;
            }

            _connectionPool.Add(Context.ConnectionId, identity.SubjectId, identity.UserId, identity.AppId);

            var groupIds = await _groupService.GetUserGroupIdsAsync(identity.SubjectId, identity.UserId);
            foreach (var groupId in groupIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, HertaLinkGroups.GetGroupName(identity.SubjectId, groupId));
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
