namespace Viv.Nana.Core
{
    public abstract class VivEvent
    {
        /// <summary>
        /// AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 租户Id
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 用户Id
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 消息优先级
        /// </summary>
        public byte Priority { get; set; }
    }
}
