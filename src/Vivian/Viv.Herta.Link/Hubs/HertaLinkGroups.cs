namespace Viv.Herta.Link.Hubs
{
    public static class HertaLinkGroups
    {
        public static string GetGroupName(long tenantId, long groupId) => $"group:{tenantId}:{groupId}";
    }
}
