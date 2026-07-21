namespace Viv.Entity.Enums
{
    /// <summary>
    /// 公告可见范围类型
    /// </summary>
    public enum EmNoticeBindType
    {
        /// <summary>
        /// 平台全局可见
        /// </summary>
        Global = 0,

        /// <summary>
        /// 指定组织可见
        /// </summary>
        Org = 1,

        /// <summary>
        /// 指定租户可见
        /// </summary>
        Tenant = 2
    }
}