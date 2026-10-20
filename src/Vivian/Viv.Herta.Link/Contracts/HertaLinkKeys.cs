namespace Viv.Herta.Link.Contracts
{
    /// <summary>
    /// SignalR 用户键 / 群名的唯一来源。两段都带租户：
    /// 群名不带租户会让不同租户的 groupId 撞进同一个组，跨租户串消息。
    /// </summary>
    public static class HertaLinkKeys
    {
        public static string GetUserKey(long tenantId, long userId) => $"herta:{tenantId}:user:{userId}";

        public static string GetGroupName(long tenantId, long groupId) => $"herta:{tenantId}:group:{groupId}";
    }
}
