using Viv.Nana;

namespace Viv.EventContracts.Herta
{
    /// <summary>
    /// 群成员变更。每个 Link 副本各自处理自己那几条连接，所以消费端必须是实例广播拓扑。
    /// </summary>
    public class MembershipChangedEvent : NanaEvent
    {
        public long TenantId { get; set; }

        public long GroupId { get; set; }

        public List<long> UserIds { get; set; } = [];

        public MembershipChangeType ChangeType { get; set; }
    }
}
