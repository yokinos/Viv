using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Viv.Contracts.Enums;
using Viv.Momo.Core;

namespace Viv.Momo
{
    public static class XMagic
    {
        /// <summary>
        /// 获取表名称
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static string GetTableName<T>()
        {
            var entityType = typeof(T);
            TableAttribute? tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            return tableAttr?.Name ?? entityType.Name;
        }

        public static Expression<Func<T, bool>> AutoSpliceCommonCondition<T>(Expression<Func<T, bool>> predicate, long tenantId, long vivAppId)
        {
            Expression<Func<T, bool>> finalPredicate = predicate;
            if (typeof(T).IsAssignableFrom(typeof(EntityBase)))
            {
                Expression<Func<T, bool>> softDeleteExpr = x => (x as EntityBase).IsDeleted == VivBool.False;
                Expression<Func<T, bool>> tenantExpr = x => (x as EntityBase).TenantId == tenantId;
                Expression<Func<T, bool>> appIdExpr = x => (x as EntityBase).VivAppId == vivAppId;

                finalPredicate = CombineExpressions(finalPredicate, softDeleteExpr);
                finalPredicate = CombineExpressions(finalPredicate, tenantExpr);
                finalPredicate = CombineExpressions(finalPredicate, appIdExpr);
            }

            return finalPredicate;
        }

        private static Expression<Func<T, bool>> CombineExpressions<T>(Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var combined = Expression.AndAlso(
                new ParameterReplacer(param).Visit(expr1.Body),
                new ParameterReplacer(param).Visit(expr2.Body)
            );
            return Expression.Lambda<Func<T, bool>>(combined, param);
        }

    }
}
