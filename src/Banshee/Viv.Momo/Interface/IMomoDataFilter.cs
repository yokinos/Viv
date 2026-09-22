using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Viv.Momo.Core;
using Viv.Momo.Enums;

namespace Viv.Momo.Interface
{
    /// <summary>
    /// 一条全局读过滤器
    ///
    /// 每条过滤器自己知道名字、管不管某个实体、EF 表达式怎么写、原生 SQL 条件怎么拼、自己有没有被关掉。
    /// 框架只负责遍历：EFAppContext 建模型、MomoDatabaseContext 拼按主键查询都不认识具体是哪几条。
    /// 加一条过滤器就是实现这个接口、再在 MomoDataFilters.All 挂一个。
    ///
    /// 开关是 IDataFilter：_dataFilter.Disable&lt;TenantDataFilter&gt;() 关掉租户过滤，作用域结束自动恢复。
    /// </summary>
    public interface IMomoDataFilter
    {
        /// <summary>
        /// EF 具名查询过滤器的 Key，也是 IDataFilter 开关的标识。改名等于改契约。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 结构上管不管这个实体，只看实体有没有实现对应的能力接口，与上下文无关
        /// </summary>
        bool AppliesTo(Type entityType);

        /// <summary>
        /// 当前上下文下挂不挂得上。租户那条没有租户访问器就不挂，其余恒为 true
        /// </summary>
        bool CanApply(EFAppContext context);

        /// <summary>
        /// 这条过滤器当前有没有被 IDataFilter 关掉
        /// </summary>
        bool IsDisabled { get; }

        /// <summary>
        /// EF 全局查询过滤器的表达式，形如 e =&gt; 关掉了 || 命中条件
        ///
        /// 「关掉了」「当前租户」这类每次查询要重取的值，只能经 context 实例上的成员读（见 DataFilterExpression）。
        /// 把过滤器自己或任何外部对象当常量捕获进表达式，会被 EF 烘进缓存的查询计划，
        /// 值冻在该查询形状第一次编译那一刻，之后换请求换租户都不变。
        /// </summary>
        LambdaExpression BuildExpression(EFAppContext context, Type entityType);

        /// <summary>
        /// 原生 SQL 的 WHERE 尾巴，形如 " AND [TenantId] = @TenantId"（含前导 AND），无条件返回空串。
        /// 带参数的过滤器自己往 parameters 里放，调用方负责交给 Dapper。
        ///
        /// tenantId 是本次读生效的租户（MomoDatabase.TenantId，ChangeTenant 的覆盖优先），
        /// 不要改成直接读请求上下文，那会把后台任务换租户读的覆盖废掉。
        /// </summary>
        string BuildSqlCondition(long tenantId, DatabaseSourceType databaseSource, IDictionary<string, object> parameters);
    }
}
