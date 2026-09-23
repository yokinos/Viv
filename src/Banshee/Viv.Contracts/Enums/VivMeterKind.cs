namespace Viv.Contracts.Enums
{
    /// <summary>
    /// 指标读数的语义分类，决定快照里那几个数怎么解读。
    ///
    /// 这是本框架自己的枚举，不是 System.Diagnostics.Metrics 里的类型 —— Viv.Contracts
    /// 不引用那个程序集，管理后台只要引 Contracts 就能拿到完整形状。
    /// </summary>
    public enum VivMeterKind
    {
        /// <summary>
        /// 累计量，读数只增不减（也可能上下浮动，如 UpDownCounter）。Count 是测量次数，Sum 是总量。
        /// </summary>
        Counter = 0,

        /// <summary>
        /// 耗时之类的一批样本。Count 是样本数，Sum 是总和，Min / Max 是这一次清零以来的极值。
        /// </summary>
        Histogram = 1,

        /// <summary>
        /// 当前水位。Count 恒为 1，Sum 与 Min / Max 都是同一个最新值 —— 它问的是「现在是几」，
        /// 不是「一共多少」，所以每次测量覆盖上一次而不是累加。
        /// </summary>
        Gauge = 2
    }
}
