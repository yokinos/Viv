namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 关闭路径上的三个时限。默认值与 <see cref="Options.KinnaOptions"/> 的默认配置一致，
    /// 单独抽出来是为了注册表在测试里能直接给定值，不必构造一整个选项对象。
    ///
    /// 三者的关系不能反：<see cref="CloseFrame"/> 是「等一帧写出去」的上限，
    /// <see cref="TerminateGrace"/> 和 <see cref="ShutdownGrace"/> 是「等整条连接收尾」的上限。
    /// 后者必须更短——否则先到的宽限会把还在等的关闭帧判成失败，指标上就多一条假的
    /// <c>close_timeout</c>。
    /// </summary>
    public sealed record KinnaWebSocketTimeouts(
        TimeSpan CloseFrame,
        TimeSpan TerminateGrace,
        TimeSpan ShutdownGrace)
    {
        public static KinnaWebSocketTimeouts Default { get; } = new(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(200));
    }
}
