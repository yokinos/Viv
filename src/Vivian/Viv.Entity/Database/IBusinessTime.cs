using System;

namespace Viv.Entity.Database
{
    /// <summary>
    /// 定义业务发生时间的约定接口。
    /// <para>
    /// 业务实际产生/发生的时间，区别于记录入库时间 <see cref="Momo.Interface.ICreatedAt"/>。
    /// </para>
    /// <para>
    /// 适用于流水、订单、统计等需要记录业务发生时刻的实体。
    /// 可存储日期或日期+时间，具体精度由业务决定。
    /// </para>
    /// </summary>
    public interface IBusinessTime
    {
        DateTime? BusinessTime { get; set; }
    }
}
