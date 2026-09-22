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
    /// 租户隔离过滤器：ITenant 实体默认只看得见当前租户的行
    ///
    /// 与别的过滤器相反，无请求上下文时不过滤 —— 不知道是谁就别猜，免得静默搞坏后台任务。
    /// HTTP 路径由 VivContextMiddleware 保证必有上下文，所以请求侧跨租户读仍被拦住。
    /// </summary>
    public sealed class TenantDataFilter : IMomoDataFilter
    {
        public string Name => "tenant";

        public bool AppliesTo(Type entityType) => typeof(ITenant).IsAssignableFrom(entityType);

        public bool CanApply(EFAppContext context) => context.HasTenantAccessor;

        public bool IsDisabled => DataFilterSwitch.IsDisabled(typeof(TenantDataFilter));

        public LambdaExpression BuildExpression(EFAppContext context, Type entityType)
        {
            var e = Expression.Parameter(entityType, "e");

            var body = Expression.OrElse(
                DataFilterExpression.Disabled(context, typeof(TenantDataFilter)),
                Expression.OrElse(
                    DataFilterExpression.Member(context, nameof(EFAppContext.HasNoTenantContext)),
                    Expression.Equal(
                        Expression.PropertyOrField(e, nameof(ITenant.TenantId)),
                        DataFilterExpression.Member(context, nameof(EFAppContext.CurrentTenantId)))));

            return Expression.Lambda(body, e);
        }

        public string BuildSqlCondition(long tenantId, DatabaseSourceType databaseSource, IDictionary<string, object> parameters)
        {
            // 与 EF 那条同一语义：没有租户就不加条件，不凭空拼一个 TenantId = 0 进去
            if (tenantId <= 0) return string.Empty;

            parameters[nameof(ITenant.TenantId)] = tenantId;
            return $" AND {SqlMagic.QuoteIdentifier(nameof(ITenant.TenantId), databaseSource)} = @{nameof(ITenant.TenantId)}";
        }
    }
}
