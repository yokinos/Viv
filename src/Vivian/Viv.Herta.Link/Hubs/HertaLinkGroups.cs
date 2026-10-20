using Viv.Herta.Link.Contracts;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// SignalR 组名。
    /// 保留这个入口只为兼容既有调用点，真正的拼接规则在 <see cref="HertaLinkKeys"/>。
    /// </summary>
    public static class HertaLinkGroups
    {
        /// <summary>
        /// 群组名：<c>herta:{tenantId}:group:{groupId}</c>。
        /// 带租户：否则两个租户的 groupId 会撞进同一个组，跨租户串消息。
        /// </summary>
        public static string GetGroupName(long tenantId, long groupId) => HertaLinkKeys.GetGroupName(tenantId, groupId);
    }
}
