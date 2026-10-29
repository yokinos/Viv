namespace Viv.Skirk.Core
{
    /// <summary>
    /// 任务状态。
    ///
    /// 用 <c>int</c> 作底层类型（不是 byte）：byte 枚举在 Dapper 里按底层类型拆箱，
    /// 列类型稍有偏差就抛 <c>Int32 → Byte</c> 转换异常（建表 DDL 那边踩过）。
    /// </summary>
    public enum VivAsyncTaskStatus
    {
        /// <summary>待执行（含等待下次重试）</summary>
        Pending = 0,

        /// <summary>执行中（已被某个副本认领）</summary>
        Running = 1,

        /// <summary>成功</summary>
        Succeeded = 2,

        /// <summary>失败（重试次数耗尽，或不可重试的错误）</summary>
        Failed = 3,

        /// <summary>已取消</summary>
        Canceled = 4,
    }
}
