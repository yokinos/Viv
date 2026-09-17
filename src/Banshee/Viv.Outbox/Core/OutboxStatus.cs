namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱行的状态。<b>数值即库里的值</b>（TINYINT / SMALLINT），改动等于改表数据。
    /// </summary>
    public enum OutboxStatus
    {
        /// <summary>待投递（含重试排期中，见 NextRetryAt）</summary>
        Pending = 0,

        /// <summary>已被某个投递器实例认领，持有 LeaseUntil 之前的租约</summary>
        Processing = 1,

        /// <summary>已投递上线</summary>
        Sent = 2,

        /// <summary>重试耗尽，等待人工介入（绝不静默丢）</summary>
        Failed = 3,
    }
}
