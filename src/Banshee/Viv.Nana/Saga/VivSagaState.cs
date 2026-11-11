namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 状态基类 —— Wolverine 模型下 **saga 类本身就是持久化状态**（直接继承 <c>Wolverine.Saga</c>，
    /// 不拆 State + StateMachine 两半）。
    ///
    /// 注意：这里必须写全限定 <c>Wolverine.Saga</c>。本命名空间就叫 <c>Viv.Nana.Saga</c>，
    /// 裸写 <c>Saga</c> 会被解析成命名空间段，编译直接报 CS0118。
    ///
    /// 三条约定，缺一条这个 saga 就跑不起来，而且失败方式都不明显：
    /// <list type="number">
    /// <item>
    /// <b>关联字段</b>：子类用 <see cref="Wolverine.Persistence.Sagas.SagaIdentityAttribute"/> 标记**唯一**一个属性
    /// （例如 <c>[SagaIdentity] public Guid OrderId</c>），消息里有同名同类型的字段即可按它关联到同一个 saga。
    /// 没有这个标记时 <see cref="VivSagaDbContext"/> 配不出主键、建表器会直接拒绝建表 —— 别指望 EF 能自己推断。
    /// </item>
    /// <item>
    /// <b>表名</b>：一个子类一张表，名字取自 <c>dialect.ToPhysical("Saga_{类名}")</c>。
    /// SqlServer 下是 <c>Saga_OrderSaga</c>，PostgreSQL 下是 <c>saga_order_saga</c> ——
    /// PG 的 <c>Quote</c> 是直通不加引号，留驼峰会被折叠成全小写，EF 之后按原名就找不到了。
    /// </item>
    /// <item>
    /// <b>建表</b>：EF 的 <c>ToTable</c> 只是映射、不会 CREATE TABLE，表由 <see cref="VivSagaTableCreator"/>
    /// 在启动时补齐（缺哪张建哪张）。漏了这一步，第一条消息会以「Invalid object name」落进死信。
    /// </item>
    /// </list>
    ///
    /// 乐观并发靠继承来的 <c>Saga.Version</c>，它在 <see cref="VivSagaDbContext"/> 里被标成并发令牌。
    /// 注意它是一列普通 <c>int</c>、**不是 RowVersion**：递增由 Wolverine 负责，EF 只负责把它放进
    /// UPDATE 的 WHERE 条件里做冲突检测。
    /// </summary>
    public abstract class VivSagaState : Wolverine.Saga
    {
        /// <summary>
        /// 发起分布式事务的客户端 AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 数据隔离标识
        /// </summary>
        public long SubjectId { get; set; }
    }
}
