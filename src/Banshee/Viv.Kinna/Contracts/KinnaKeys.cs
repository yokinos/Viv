namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// SignalR 用户 Key、群名和投递水位的生成器。
    ///
    /// 加组和发组必须用同一个实例。规则分成两份迟早对不上，对不上时不会报错，
    /// 只是消息发不到人。前缀在构造时定下，多 Hub 各用各的。
    /// </summary>
    public interface IKinnaKeys
    {
        string UserKey(long subjectId, long userId);

        /// <summary>群名带租户，否则两个租户的 groupId 会撞进同一个组。</summary>
        string GroupName(long subjectId, long groupId);

        /// <summary>某个用户已确认到哪条消息（投递确认的水位）。</summary>
        string DeliveryKey(long subjectId, long userId);
    }

    /// <summary>默认实现，前缀为空时用 "kinna"。</summary>
    public sealed class KinnaKeys : IKinnaKeys
    {
        private readonly string _prefix;

        public KinnaKeys(string? prefix)
        {
            _prefix = string.IsNullOrWhiteSpace(prefix) ? "kinna" : prefix;
        }

        public string UserKey(long subjectId, long userId) => $"{_prefix}:{subjectId}:user:{userId}";

        public string GroupName(long subjectId, long groupId) => $"{_prefix}:{subjectId}:group:{groupId}";

        public string DeliveryKey(long subjectId, long userId) => $"{_prefix}:{subjectId}:delivered:{userId}";
    }
}
