namespace Viv.Apex.Core.Entity.Vo.App
{
    public class ClientAppPageOutput
    {
        public int PageIndex { get; set; }

        public int PageSize { get; set; }

        public long TotalCount { get; set; }

        public List<ClientAppListItem> Items { get; set; } = [];
    }
}
