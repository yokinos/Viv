namespace Viv.Delusion.Snowflake
{
    /// <summary>
    /// 框架默认 ID 生成器：把优化版 <see cref="LeafSnowflakeIdGenerator"/> 适配成统一的 <see cref="IIdGenerator"/>。
    /// </summary>
    /// <remarks>
    /// 为什么需要这一层适配，而不是让 <see cref="LeafSnowflakeIdGenerator"/> 直接实现 <see cref="IIdGenerator"/>：
    /// 两个算法实现（标准版 <see cref="SnowflakeIdGenerator"/>、优化版 <see cref="LeafSnowflakeIdGenerator"/>）
    /// 都是独立可复用的算法资产，不绑定框架契约 —— 谁想在别的项目里单独拿走用，不该被迫拖上 IIdGenerator。
    ///
    /// 本类只做转发，不含任何状态与逻辑；<b>线程安全由被包装的实例保证</b>，本类本身无可变字段。
    /// </remarks>
    public sealed class DefaultIdGenerator : IIdGenerator
    {
        private readonly LeafSnowflakeIdGenerator _inner;

        /// <summary>
        /// 按机器号创建，位宽与回拨容忍取 <see cref="SnowflakeOptions"/> 默认值（10 机器位 + 12 序列位，容忍 5ms 回拨）。
        /// </summary>
        /// <param name="machineId">机器号。装配方应传宿主自己的号（Viv 取 <c>EnvOption.MachineId</c>）</param>
        public DefaultIdGenerator(long machineId)
            : this(new SnowflakeOptions { MachineId = machineId })
        {

        }

        /// <summary>
        /// 按完整配置创建。
        /// </summary>
        /// <param name="options">位宽、纪元、机器号、回拨容忍</param>
        public DefaultIdGenerator(SnowflakeOptions options)
        {
            _inner = new LeafSnowflakeIdGenerator(options);
        }

        /// <inheritdoc />
        public long NextId() => _inner.NextId();
    }
}
