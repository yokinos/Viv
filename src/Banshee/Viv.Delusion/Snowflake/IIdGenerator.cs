namespace Viv.Delusion.Snowflake
{
    /// <summary>
    /// 全局唯一 ID 生成器。Viv 全框架的 ID 出口。
    /// </summary>
    /// <remarks>
    /// 接口与实现同放 <c>Viv.Delusion.Snowflake</c>：<c>Viv.Contracts</c> 引用 <c>Viv.Delusion</c> 而非反过来，
    /// 接口若住在 Contracts，Delusion 里的实现就 implements 不了它（循环引用）。
    /// 需要注入此接口的项目（Contracts / Engine / 各业务 Core）都已直接或传递引用 Delusion。
    ///
    /// 只暴露「给我一个 ID」这一件事。<b>不接收机器号参数</b> —— 机器号是部署期身份，
    /// 由装配方从 <c>EnvOption.MachineId</c> 注入实现，调用方无从也无权指定；
    /// 允许调用方传机器号等于允许业务代码伪造节点身份来绕开唯一性。
    ///
    /// 实现负责：毫秒内序列自增、序列溢出等待下一毫秒、时钟回拨处理。
    /// 调用方只需注意：雪花 ID 可反解出生成时间，不要当作不可推测的凭据使用。
    /// </remarks>
    public interface IIdGenerator
    {
        /// <summary>
        /// 生成一个 64 位、全局唯一、同一生成器实例内单调递增的 ID。
        /// </summary>
        /// <returns>雪花 ID（long）。高位是毫秒时间戳。</returns>
        /// <exception cref="System.InvalidOperationException">时钟回拨超出容忍范围时抛出，此时不会返回可能重复的 ID。</exception>
        long NextId();
    }
}
