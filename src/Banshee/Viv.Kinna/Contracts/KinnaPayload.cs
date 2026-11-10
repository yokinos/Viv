namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 下行载荷的基类，目前只承载协议版本号，业务字段都在子类。
    /// 破坏性变更时 V 加一；当前恒为 1，没有任何代码读它做分支——只是给将来留的位置。
    /// </summary>
    public abstract record KinnaPayload
    {
        public int V { get; init; } = 1;
    }
}
