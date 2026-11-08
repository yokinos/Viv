namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 下行帧（服务端 → 客户端）。原生 WebSocket 没有 SignalR 的 target/arguments 封装，
    /// 所以自己定一层最小信封：<c>{"method":"ReceiveMessage","args":[{...}]}</c>。
    ///
    /// 用 method/args 而不是 type/data，是为了让两种传输的下行语义一一对应：
    /// 业务侧 <see cref="Dispatch.IKinnaPusher{TEvent}"/> 产出的方法名和载荷原样落到这里，
    /// 不需要知道客户端用的是 SignalR 还是裸 WebSocket。
    /// </summary>
    /// <param name="Method">方法名，取 <see cref="KinnaClientMethods"/></param>
    /// <param name="Args">参数数组，与 SignalR 的 arguments 对齐</param>
    public sealed record KinnaServerEnvelope(string Method, object?[] Args);
}
