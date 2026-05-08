using Viv.Nana.Saga;

namespace Viv.Apex.Core.Saga
{
    /// <summary>
    /// 订单 Saga 的 EF Core 表映射（表名：Saga_Order）
    /// </summary>
    public class OrderSagaClassMap : VivSagaClassMap<OrderSagaState>
    {
        // VivSagaClassMap 已处理 CorrelationId / CurrentState / AppId / TenantId / RowVersion
        // 此处可覆盖以添加自定义字段映射（如设置表名）
    }
}
