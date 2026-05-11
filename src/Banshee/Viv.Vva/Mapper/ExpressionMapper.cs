using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Viv.Vva.Mapper
{
    public static class ExpressionMapper
    {
        public static bool IsEnabled = false;

        private static readonly Dictionary<string, Delegate> _compiledCache = new();

        public static TTarget Map<TTarget>(object source)
        {
            return (TTarget)Map(source, typeof(TTarget));
        }

        private static object? Map(object source, Type targetType)
        {
            try
            {
                if (source == null) return null;
                Type sourceType = source.GetType();

                string key = $"MAP_{sourceType.FullName}_{targetType.FullName}";
                if (!_compiledCache.TryGetValue(key, out var lambda))
                {
                    lambda = BuildLambda(sourceType, targetType).Compile();
                    _compiledCache[key] = lambda;
                }
                return lambda.DynamicInvoke(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static LambdaExpression BuildLambda(Type sourceType, Type targetType)
        {
            ParameterExpression sourceParam = Expression.Parameter(sourceType, "s");
            LabelTarget returnLabel = Expression.Label(targetType);

            ParameterExpression targetVar = Expression.Variable(targetType, "t");
            var body = new List<Expression>
        {
            Expression.Assign(targetVar, Expression.New(targetType))
        };

            foreach (PropertyInfo sourceProp in sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!sourceProp.CanRead) continue;

                PropertyInfo targetProp = FindMatchingProperty(targetType, sourceProp.Name);
                if (targetProp == null || !targetProp.CanWrite) continue;

                Type sType = sourceProp.PropertyType;
                Type tType = targetProp.PropertyType;

                MemberExpression value = Expression.Property(sourceParam, sourceProp);

                // 可直接赋值的类型（相同类型、派生类赋给基类等）
                if (tType.IsAssignableFrom(sType))
                {
                    body.Add(Expression.Assign(Expression.Property(targetVar, targetProp), value));
                    continue;
                }

                // 集合 → 递归映射每个元素
                if (IsEnumerable(sType) && IsEnumerable(tType))
                {
                    Type sElem = sType.GetGenericArguments()[0];
                    Type tElem = tType.GetGenericArguments()[0];

                    // 递归获取元素映射的 LambdaExpression
                    LambdaExpression itemLambda = BuildLambda(sElem, tElem);
                    ParameterExpression xParam = Expression.Parameter(sElem, "x");
                    Expression mappedItem = Expression.Invoke(itemLambda, xParam);

                    LambdaExpression selectLambda = Expression.Lambda(mappedItem, xParam);

                    MethodInfo selectMethod = typeof(Enumerable)
                        .GetMethods().First(m => m.Name == "Select" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(sElem, tElem);

                    MethodInfo toListMethod = typeof(Enumerable)
                        .GetMethods().First(m => m.Name == "ToList")
                        .MakeGenericMethod(tElem);

                    Expression selectCall = Expression.Call(selectMethod, value, selectLambda);
                    Expression toListCall = Expression.Call(toListMethod, selectCall);

                    body.Add(Expression.Assign(Expression.Property(targetVar, targetProp), toListCall));
                    continue;
                }

                // 自定义对象 → 深度递归映射
                if (IsCustomType(sType) && IsCustomType(tType))
                {
                    LambdaExpression subLambda = BuildLambda(sType, tType);
                    Expression subValue = Expression.Invoke(subLambda, value);
                    body.Add(Expression.Assign(Expression.Property(targetVar, targetProp), subValue));
                    continue;
                }
            }

            body.Add(Expression.Return(returnLabel, targetVar, targetType));
            body.Add(Expression.Label(returnLabel, targetVar));

            BlockExpression block = Expression.Block(new[] { targetVar }, body);
            return Expression.Lambda(block, sourceParam);
        }

        public static bool IsCustomType(Type type)
        {
            return !type.IsPrimitive && type != typeof(string) && !type.IsEnum && !type.IsValueType;
        }

        public static bool IsEnumerable(Type type)
        {
            return typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
        }

        private static string SnakeToPascal(string snake)
        {
            if (!snake.Contains('_')) return snake;
            var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
        }

        private static string PascalToSnake(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i]))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        private static PropertyInfo FindMatchingProperty(Type targetType, string sourcePropName)
        {
            var allProps = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToArray();

            var prop = allProps.FirstOrDefault(p => p.Name == sourcePropName);
            if (prop != null) return prop;

            if (sourcePropName.Contains('_'))
            {
                var pascal = SnakeToPascal(sourcePropName);
                prop = allProps.FirstOrDefault(p => string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop;
            }

            if (sourcePropName.Any(char.IsUpper))
            {
                var snake = PascalToSnake(sourcePropName);
                prop = allProps.FirstOrDefault(p => string.Equals(p.Name, snake, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop;
            }

            return allProps.FirstOrDefault(p => string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase));
        }
    }
}