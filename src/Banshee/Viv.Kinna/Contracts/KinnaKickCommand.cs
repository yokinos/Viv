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
        /// <summary>租户。必填，小于等于 0 时订阅端直接丢弃</summary>
        public long SubjectId { get; set; }

        /// <summary>用户。为 0 表示踢该租户下的全部连接</summary>
        public long UserId { get; set; }

        /// <summary>
        /// 客户端。为 0 表示该用户的**所有**客户端（封号、强制下线）；
        /// 大于 0 只踢这一端（某一端 token 泄露、某一端版本有 bug 要强制重新登录）。
        ///
        /// 两端并存时 AppId 必须不同，所以这个字段才区分得开
        /// </summary>
        public long AppId { get; set; }

        /// <summary>原因，只进日志</summary>
        public string? Reason { get; set; }

        /// <summary>
        /// 发布方实例 Id。订阅端发现是自己的消息就跳过——
        /// 发布方已经先踢过本机了，再踢一遍只会多记一次 terminated 指标
        /// </summary>
        public string? Origin { get; set; }
    }
}
