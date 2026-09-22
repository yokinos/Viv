using System;
using System.Linq.Expressions;
using System.Reflection;
using Viv.Momo.Core;

namespace Viv.Momo.DataFilter
{
    /// <summary>
    /// 过滤器表达式里读「每次查询要重取的值」的唯一入口
    ///
    /// 必须经 EFAppContext 实例读。EF 会把上下文常量换成当前这个上下文并按查询重新求值，
    /// 换成捕获过滤器自己或别的外部对象，就会被烘进缓存的查询计划，
    /// 值停在那个查询形状第一次编译那一刻，换请求换租户都不变。
    /// </summary>
    internal static class DataFilterExpression
    {
        /// <summary>
        /// EFAppContext.IsDataFilterDisabled 的 MethodInfo，供表达式拼接用
        /// </summary>
        private static readonly MethodInfo IsDisabledMethod =
            typeof(EFAppContext).GetMethod(nameof(EFAppContext.IsDataFilterDisabled))!;

        /// <summary>
        /// 当前上下文
        /// </summary>
        internal static Expression Context(EFAppContext context)
            => Expression.Constant(context, typeof(EFAppContext));

        /// <summary>
        /// 这条过滤器被关掉了吗
        /// </summary>
        internal static Expression Disabled(EFAppContext context, Type filterType)
            => Expression.Call(Context(context), IsDisabledMethod, Expression.Constant(filterType, typeof(Type)));

        /// <summary>
        /// 读上下文上的一个属性
        /// </summary>
        internal static Expression Member(EFAppContext context, string propertyName)
            => Expression.Property(Context(context), propertyName);
    }
}
