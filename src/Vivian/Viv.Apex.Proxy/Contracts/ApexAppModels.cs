using MessagePack;

namespace Viv.Apex.Proxy.Contracts
{
    /// <summary>
    /// 线格式 DTO。<c>[Key(n)]</c> 就是 proto 的字段号：
    /// <b>发出去之后不许改号、不许复用</b>，删除字段时把号空着（等价 proto 的 reserved）。
    /// </summary>

    [MessagePackObject]
    public sealed class ValidateClientAppRequest
    {
        /// <summary>客户端应用 Id，对应 AtClientApp.Id</summary>
        [Key(0)]
        public long AppId { get; set; }
    }

    [MessagePackObject]
    public sealed class ValidateClientAppReply
    {
        [Key(0)]
        public bool Exists { get; set; }

        [Key(1)]
        public bool Enabled { get; set; }

        [Key(2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>不可用时的原因，可直接写进日志</summary>
        [Key(3)]
        public string Message { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public sealed class GetClientAppPageRequest
    {
        /// <summary>页码，从 1 开始</summary>
        [Key(0)]
        public int PageIndex { get; set; }

        /// <summary>每页条数，服务端会做上下限收敛</summary>
        [Key(1)]
        public int PageSize { get; set; }
    }

    [MessagePackObject]
    public sealed class ClientAppItem
    {
        [Key(0)]
        public long Id { get; set; }

        [Key(1)]
        public string AppKey { get; set; } = string.Empty;

        [Key(2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>EmAppPlatform 的值</summary>
        [Key(3)]
        public int Platform { get; set; }

        /// <summary>EmClientStack 的值</summary>
        [Key(4)]
        public int ClientStack { get; set; }

        [Key(5)]
        public bool Enabled { get; set; }
    }

    [MessagePackObject]
    public sealed class GetClientAppPageReply
    {
        [Key(0)]
        public int TotalCount { get; set; }

        [Key(1)]
        public int TotalPages { get; set; }

        [Key(2)]
        public List<ClientAppItem> Items { get; set; } = [];
    }
}
