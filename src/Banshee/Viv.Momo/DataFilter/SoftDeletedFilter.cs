using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Viv.Contracts.Interface;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.DataFilter
{
    /// <summary>
    /// 软删除过滤器：ISoftDeleted 实体默认看不见 IsDeleted = 1 的行
    ///
    /// 不学租户那条的「无上下文就不过滤」。租户是不知道是谁所以别猜，
    /// 软删除没这层歧义，IsDeleted = 1 就是删了，后台任务同样不该看见。
    /// </summary>
    public sealed class SoftDeletedFilter : IMomoDataFilter
    {
        public string Name => "softdelete";

        public bool AppliesTo(Type entityType) => typeof(ISoftDeleted).IsAssignableFrom(entityType);

        public bool CanApply(EFAppContext context) => true;

        public bool IsDisabled => DataFilterSwitch.IsDisabled(typeof(SoftDeletedFilter));

        public LambdaExpression BuildExpression(EFAppContext context, Type entityType)
        {
            var e = Expression.Parameter(entityType, "e");

            var body = Expression.OrElse(
                DataFilterExpression.Disabled(context, typeof(SoftDeletedFilter)),
                Expression.Not(Expression.PropertyOrField(e, nameof(ISoftDeleted.IsDeleted))));

            return Expression.Lambda(body, e);
        }

        public string BuildSqlCondition(long tenantId, DatabaseSourceType databaseSource, IDictionary<string, object> parameters)
        {
            // 布尔字面量按方言给：PostgreSQL 的 boolean 列不接受 `= 0`（与 GetSoftDeleteSql 同一取舍）
            var falseValue = databaseSource == DatabaseSourceType.PostgreSQL ? "false" : "0";
            return $" AND {SqlMagic.QuoteIdentifier(nameof(ISoftDeleted.IsDeleted), databaseSource)} = {falseValue}";
        }
    }
}
