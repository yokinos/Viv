namespace Viv.Apex.Core.Entity.Vo.App
{
    /// <summary>应用分页结果。行里不含密钥。</summary>
    public class ClientAppPageOutput
    {
        /// <summary>当前页码，从 1 开始。</summary>
        public int PageIndex { get; set; }

        /// <summary>每页条数。</summary>
        public int PageSize { get; set; }

        /// <summary>符合条件的总条数。</summary>
        public long TotalCount { get; set; }

        /// <summary>当前页的应用行。</summary>
        public List<ClientAppListItem> Items { get; set; } = [];
    }
}
