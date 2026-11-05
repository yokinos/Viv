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

        /// <summary>
        /// 归属主体 Id：<see cref="UserType"/> 为 OrgUser / CompanyUser 时填 AtOrg.Id / AtCompany.Id，
        /// TenantUser / EndUser 填 AtTenant.Id。
        /// <b>Master 也照填</b>——它必须落在某个租户下才有租户上下文，但服务端不会拿它查授权。
        /// </summary>
        [Key(4)]
        public long SubjectId { get; set; }

        /// <summary>
        /// <c>Viv.Entity.Enums.EmUserType</c> 的值。
        /// <b>-1 表示「只校验应用本身，不做归属校验」</b>，供连接准入这类手上没有用户类型的场景使用。
        /// 默认值必须是 -1 而不是 0：0 是 Master，未赋值时会被当成「免校验」。
        /// </summary>
        [Key(5)]
        public int UserType { get; set; } = -1;
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

        /// <summary>
        /// 调用方是否可以放行：应用存在、启用，且（请求带了归属信息时）该主体确实被授权。
        /// 与 <see cref="Enabled"/> 分开是因为「应用被禁用」和「这个主体没买这个应用」是两回事，
        /// 混用一个字段会让日志看不出该找谁。
        /// </summary>
        [Key(4)]
        public bool Allowed { get; set; }
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
