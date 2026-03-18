using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Viv.Momo.Enums;

namespace Viv.Momo.Converter
{
    public class SqlExpressionVisitor : ExpressionVisitor
    {
        public StringBuilder Sql { get; } = new();
        public Dictionary<string, object> Parameters { get; } = [];
        private int _paramIndex;

        // 二元运算：== && || > < >= <= !=
        protected override Expression VisitBinary(BinaryExpression node)
        {
            Sql.Append("(");
            Visit(node.Left);
            Sql.Append(GetOperator(node.NodeType));
            Visit(node.Right);
            Sql.Append(")");
            return node;
        }

        // 字段访问：x.Id, x.Name
        protected override Expression VisitMember(MemberExpression node)
        {
            // 外部变量（闭包）→ 转为参数
            if (node.Expression is ConstantExpression)
            {
                AddParameter(GetConstantValue(node));
                return node;
            }

            // 数据库字段
            Sql.Append(node.Member.Name);
            return node;
        }

        // 常量
        protected override Expression VisitConstant(ConstantExpression node)
        {
            AddParameter(node.Value);
            return node;
        }

        // 方法调用：如 x.Name.Contains("xx")
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Contains" && node.Object?.Type == typeof(string))
            {
                Visit(node.Object);
                Sql.Append(" LIKE ");
                var value = GetConstantValue(node.Arguments[0]);
                AddParameter($"%{value}%");
                return node;
            }

            throw new NotSupportedException($"不支持方法 {node.Method.Name}");
        }


        private void AddParameter(object value)
        {
            var paramName = $"@p{_paramIndex++}";
            Parameters[paramName] = value;
            Sql.Append(paramName);
        }

        private static object GetConstantValue(Expression expression)
        {
            var unary = Expression.Convert(expression, typeof(object));
            return Expression.Lambda<Func<object>>(unary).Compile()();
        }

        private string GetOperator(ExpressionType type) => type switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.LessThan => " < ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"不支持运算符 {type}")
        };
    }
}
