namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 跨副本踢人指令。走控制面广播，不走业务事件总线：
    /// 封号要立刻生效，和业务消息共用一条队列，赶上半岛积压就变成分钟级；
    /// 而丢一条的代价只是多连几十秒——Ping 拿不到续租会自己断。
    ///
    /// 用类而不是 record：Redis 的序列化走 Newtonsoft，带构造参数的 record
    /// 依赖构造器绑定，收敛成可写属性最稳。
    /// </summary>
    public sealed class KinnaKickCommand
    {
        /// <summary>
        /// 租户。除「按客户端踢」外必填，小于等于 0 时订阅端当脏消息丢弃。
        ///
        /// 唯一的例外是 <see cref="AppId"/> &gt; 0 且 <see cref="UserId"/> 为 0 的情况：
        /// 那表示踢掉这个客户端的全部连接（跨租户），此时租户留 0。
        /// </summary>
        public long SubjectId { get; set; }

        /// <summary>用户。为 0 表示踢该租户下的全部连接</summary>
        public long UserId { get; set; }

        /// <summary>
        /// 客户端。为 0 表示该用户的**所有**客户端（封号、强制下线）；
        /// 大于 0 只踢这一端（某一端 token 泄露、某一端版本有 bug 要强制重新登录）。
        ///
        /// 两端并存时 AppId 必须不同，所以这个字段才区分得开。
        ///
        /// <see cref="UserId"/> 也是 0 时语义升级成「这个客户端的全部连接，不分租户」，
        /// 见 <see cref="SubjectId"/>。
        /// </summary>
        public long AppId { get; set; }

        /// <summary>原因，只进日志</summary>
        public string? Reason { get; set; }

        /// <summary>
        /// 顶号专用：那条**新**连接的建立时间。有值表示「只踢比它更旧的连接」，
        /// 为空表示管理动作（封号、强制下线），按 <see cref="AppId"/> / <see cref="UserId"/> 的粒度全踢。
        ///
        /// 为什么顶号要带时间：新连接到 A、旧连接在 B，A 广播出去时 B 无法判断
        /// 自己那条到底是「该被顶掉的旧连接」还是「另一个并发上来的新连接」——
        /// 全踢会让两条新连接互相踢掉，用户看到连上就被断、断了又连。
        /// </summary>
        public DateTimeOffset? EstablishedAt { get; set; }

        /// <summary>
        /// 顶号时那条新连接的 Id，和 <see cref="EstablishedAt"/> 一起构成全序。
        /// 两条连接几乎同时建立时时间可能相同，光比时间分不出先后，带上 Id 才能恰好留下一条。
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// 发布方实例 Id。订阅端发现是自己的消息就跳过——
        /// 发布方已经先踢过本机了，再踢一遍只会多记一次 terminated 指标
        /// </summary>
        public string? Origin { get; set; }
    }
}
