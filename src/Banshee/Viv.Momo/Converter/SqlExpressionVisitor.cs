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
        public Dictionary<string, object> Parameters { get; } = new();
        private int _paramIndex;
        private readonly DatabaseSouceType _databaseSourceType; // 注意枚举名称拼写

        public SqlExpressionVisitor(DatabaseSouceType databaseSourceType)
        {
            _databaseSourceType = databaseSourceType;
        }

        // 根据数据库类型引用标识符
        private string QuoteIdentifier(string name)
        {
            return _databaseSourceType switch
            {
                DatabaseSouceType.SqlServer => $"[{name}]",
                DatabaseSouceType.PostgreSQL => $"{name.ToLowerInvariant()}",
                _ => name
            };
        }

        // 辅助方法：检测表达式是否依赖任何参数
        private static bool ContainsParameter(Expression expr)
        {
            if (expr == null) return false;
            switch (expr.NodeType)
            {
                case ExpressionType.Parameter:
                    return true;
                case ExpressionType.MemberAccess:
                    var me = (MemberExpression)expr;
                    return ContainsParameter(me.Expression);
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    var ue = (UnaryExpression)expr;
                    return ContainsParameter(ue.Operand);
                default:
                    return false;
            }
        }

        // 获取表达式的常量值（支持闭包变量）
        private object GetConstantValue(Expression expr)
        {
            if (expr is ConstantExpression constant)
                return constant.Value;

            var lambda = Expression.Lambda<Func<object>>(Expression.Convert(expr, typeof(object)));
            return lambda.Compile()();
        }

        // 添加参数到字典，并返回参数名（不同数据库参数前缀可能不同，但当前统一用 @）
        private string AddParameter(object value)
        {
            var paramName = $"@p{_paramIndex++}";
            Parameters[paramName] = value;
            return paramName;
        }

        // 获取 SQL 运算符
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

        // 二元表达式
        protected override Expression VisitBinary(BinaryExpression node)
        {
            Sql.Append("(");
            Visit(node.Left);
            Sql.Append(GetOperator(node.NodeType));
            Visit(node.Right);
            Sql.Append(")");
            return node;
        }

        // 成员访问（字段/属性/变量）
        protected override Expression VisitMember(MemberExpression node)
        {
            // 如果整个成员表达式不依赖任何参数（例如闭包变量或常量），则直接参数化
            if (!ContainsParameter(node))
            {
                var paramName = AddParameter(GetConstantValue(node));
                Sql.Append(paramName);
                return node;
            }

            // 否则，生成数据库字段路径（例如：表别名.字段）
            if (node.Expression != null)
            {
                Visit(node.Expression);
                // 如果表达式不是参数且已有内容，添加点分隔符
                if (!(node.Expression is ParameterExpression) && Sql.Length > 0 && Sql[^1] != '(')
                    Sql.Append(".");
            }

            // 根据数据库类型引用字段名
            Sql.Append(QuoteIdentifier(node.Member.Name));
            return node;
        }

        // 常量表达式
        protected override Expression VisitConstant(ConstantExpression node)
        {
            var paramName = AddParameter(node.Value);
            Sql.Append(paramName);
            return node;
        }

        // 参数表达式（通常忽略）
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node;
        }

        // 方法调用表达式（字符串 Contains, StartsWith, EndsWith）
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(string) && node.Object != null)
            {
                switch (node.Method.Name)
                {
                    case "Contains":
                        Visit(node.Object);
                        Sql.Append(" LIKE ");
                        var containsValue = GetConstantValue(node.Arguments[0]);
                        var containsParam = AddParameter($"%{containsValue}%");
                        Sql.Append(containsParam);
                        return node;

                    case "StartsWith":
                        Visit(node.Object);
                        Sql.Append(" LIKE ");
                        var startsValue = GetConstantValue(node.Arguments[0]);
                        var startsParam = AddParameter($"{startsValue}%");
                        Sql.Append(startsParam);
                        return node;

                    case "EndsWith":
                        Visit(node.Object);
                        Sql.Append(" LIKE ");
                        var endsValue = GetConstantValue(node.Arguments[0]);
                        var endsParam = AddParameter($"%{endsValue}");
                        Sql.Append(endsParam);
                        return node;
                }
            }

            throw new NotSupportedException($"不支持方法 {node.Method.Name}");
        }
    }
}