using System.ComponentModel;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 授权点数的变动类型。
    ///
    /// <para><b>从 1 开始</b>：0 不是合法值 —— 新增字段、老数据、默认值都会落到 0，
    /// 让它可识别（报表里能筛出来当脏数据），比让 0 悄悄等于「充值」安全。</para>
    /// </summary>
    public enum EmPointChangeType : byte
    {
        /// <summary>
        /// 平台批点数（代理买货）
        /// </summary>
        [Description("充值")]
        Recharge = 1,

        /// <summary>
        /// 售卖扣减（授权落地时）
        /// </summary>
        [Description("售卖扣减")]
        Consume = 2,

        /// <summary>
        /// 退回（授权撤销/退款，点数回到池子里）
        /// </summary>
        [Description("退回")]
        Refund = 3,

        /// <summary>
        /// 人工调整（盘点、补偿、迁移）
        /// </summary>
        [Description("人工调整")]
        Adjust = 4
    }
}
