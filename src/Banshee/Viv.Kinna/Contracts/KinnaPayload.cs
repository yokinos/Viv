namespace Viv.Kinna.Contracts
{
    /// <summary>下行载荷的基类，只放协议版本。破坏性变更时 V 加一。</summary>
    public abstract record KinnaPayload
    {
        public int V { get; init; } = 1;
    }
}
