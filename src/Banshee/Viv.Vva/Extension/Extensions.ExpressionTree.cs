using System;
using System.Linq.Expressions;

namespace Viv.Vva.Extension
{
    public static partial class Extensions
    {
        /// <summary>
        /// 并且条件拼接
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        {
            if (first == null) return second;
            if (second == null) return first;

            var visitor = new ParameterReplaceVisitor(second.Parameters[0], first.Parameters[0]);
            var secondBody = visitor.Visit(second.Body);

            var body = Expression.AndAlso(first.Body, secondBody);
            return Expression.Lambda<Func<T, bool>>(body, first.Parameters);
        }

        /// <summary>
        /// 或者条件拼接
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        {
            if (first == null) return second;
            if (second == null) return first;

            var visitor = new ParameterReplaceVisitor(second.Parameters[0], first.Parameters[0]);
            var secondBody = visitor.Visit(second.Body);

            var body = Expression.OrElse(first.Body, secondBody);
            return Expression.Lambda<Func<T, bool>>(body, first.Parameters);
        }

        /// <summary>
        /// 表达式参数替换
        /// </summary>
        private class ParameterReplaceVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly ParameterExpression _newParam;

            public ParameterReplaceVisitor(ParameterExpression oldParam, ParameterExpression newParam)
            {
                _oldParam = oldParam;
                _newParam = newParam;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _oldParam ? _newParam : base.VisitParameter(node);
            }
        }
    }
}