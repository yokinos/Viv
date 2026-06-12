using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Viv.Delusion.Mapper
{
    public static class EmitMapper
    {
        // 缓存已编译的映射委托，键为 "源类型全名->目标类型全名"
        private static readonly Dictionary<string, Func<object, object>> _mapperCache = [];
        private static readonly ReaderWriterLockSlim _cacheLock = new();

        /// <summary>
        /// 将 source 映射到 TTarget 类型的新对象
        /// </summary>
        public static TTarget? Map<TTarget>(object source) //where TTarget : class, new()
        {
            if (source == null) return default;
            var mapper = GetOrCreateMapper(source.GetType(), typeof(TTarget));
            return (TTarget)mapper(source);
        }

        /// <summary>
        /// 获取或创建从 sourceType 到 targetType 的映射委托
        /// </summary>
        private static Func<object, object> GetOrCreateMapper(Type sourceType, Type targetType)
        {
            string key = $"{sourceType.FullName}->{targetType.FullName}";

            // 先尝试不加锁读取
            _cacheLock.EnterReadLock();
            try
            {
                if (_mapperCache.TryGetValue(key, out var mapper))
                    return mapper;
            }
            finally
            {
                _cacheLock.ExitReadLock();
            }

            // 未找到，加写锁创建
            _cacheLock.EnterWriteLock();
            try
            {
                // 再次检查，防止其他线程已创建
                if (_mapperCache.TryGetValue(key, out var mapper))
                    return mapper;

                mapper = BuildMapper(sourceType, targetType);
                _mapperCache[key] = mapper;
                return mapper;
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 使用 Emit 动态构建映射方法
        /// </summary>
        private static Func<object, object> BuildMapper(Type sourceType, Type targetType)
        {
            // 动态方法：object Map(object source)
            DynamicMethod method = new DynamicMethod("Map_" + targetType.Name, typeof(object), new[] { typeof(object) }, typeof(EmitMapper).Module);

            ILGenerator il = method.GetILGenerator();

            // 声明局部变量：targetType target = new targetType();
            LocalBuilder targetLocal = il.DeclareLocal(targetType);
            ConstructorInfo ctor = targetType.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                throw new InvalidOperationException($"目标类型 {targetType} 必须具有无参构造函数");
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, targetLocal);

            // 将 source 参数转换为源类型，存入局部变量
            LocalBuilder sourceLocal = il.DeclareLocal(sourceType);
            il.Emit(OpCodes.Ldarg_0);                // 加载 source 参数
            il.Emit(OpCodes.Castclass, sourceType);  // 转换为 sourceType
            il.Emit(OpCodes.Stloc, sourceLocal);

            // 遍历源类型的所有可读公共实例属性
            foreach (PropertyInfo srcProp in sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!srcProp.CanRead) continue;

                // 查找目标类型中同名可写属性（忽略大小写）
                PropertyInfo tgtProp = FindMatchingProperty(targetType, srcProp.Name);
                if (tgtProp == null || !tgtProp.CanWrite) continue;

                Type srcPropType = srcProp.PropertyType;
                Type tgtPropType = tgtProp.PropertyType;

                // 如果目标属性类型可以直接从源属性类型赋值（相同或派生类），生成直接赋值
                if (tgtPropType.IsAssignableFrom(srcPropType))
                {
                    // target.Prop = source.Prop;
                    il.Emit(OpCodes.Ldloc, targetLocal);
                    il.Emit(OpCodes.Ldloc, sourceLocal);
                    il.EmitCall(OpCodes.Callvirt, srcProp.GetMethod, null);
                    il.EmitCall(OpCodes.Callvirt, tgtProp.SetMethod, null);
                    continue;
                }

                // 处理集合：源是 IEnumerable<T>，目标为 List<T> 或数组
                if (IsEnumerable(srcPropType) && IsCollectionOrArray(tgtPropType))
                {
                    Type srcElemType = GetElementType(srcPropType);
                    Type tgtElemType = GetElementType(tgtPropType);

                    // 目标为 List<T> 还是数组？
                    bool targetIsList = tgtPropType.IsGenericType && tgtPropType.GetGenericTypeDefinition() == typeof(List<>);
                    bool targetIsArray = tgtPropType.IsArray;

                    if (targetIsList || targetIsArray)
                    {
                        // 准备调用辅助方法：MapToList 或 MapToArray
                        MethodInfo mapMethod = targetIsList
                            ? typeof(EmitMapper).GetMethod(nameof(MapToList), BindingFlags.NonPublic | BindingFlags.Static)
                            ?.MakeGenericMethod(srcElemType, tgtElemType)
                            : typeof(EmitMapper).GetMethod(nameof(MapToArray), BindingFlags.NonPublic | BindingFlags.Static)
                                ?.MakeGenericMethod(srcElemType, tgtElemType);

                        if (mapMethod == null)
                            throw new InvalidOperationException("无法获取集合映射辅助方法");

                        // target.Prop = MapXxx(source.Prop);
                        il.Emit(OpCodes.Ldloc, targetLocal);
                        il.Emit(OpCodes.Ldloc, sourceLocal);
                        il.EmitCall(OpCodes.Callvirt, srcProp.GetMethod, null);
                        il.EmitCall(OpCodes.Call, mapMethod, null);
                        il.EmitCall(OpCodes.Callvirt, tgtProp.SetMethod, null);
                        continue;
                    }
                }

                // 处理嵌套对象：双方都是复杂类型（非基元、非字符串、非集合）
                if (IsComplex(srcPropType) && IsComplex(tgtPropType))
                {
                    // 准备调用 MapNested 辅助方法
                    MethodInfo mapNestedMethod = typeof(EmitMapper)
                        .GetMethod(nameof(MapNested), BindingFlags.NonPublic | BindingFlags.Static)
                        ?.MakeGenericMethod(srcPropType, tgtPropType);

                    if (mapNestedMethod == null)
                        throw new InvalidOperationException("无法获取嵌套对象映射辅助方法");

                    // target.Prop = MapNested(source.Prop);
                    il.Emit(OpCodes.Ldloc, targetLocal);
                    il.Emit(OpCodes.Ldloc, sourceLocal);
                    il.EmitCall(OpCodes.Callvirt, srcProp.GetMethod, null);
                    il.EmitCall(OpCodes.Call, mapNestedMethod, null);
                    il.EmitCall(OpCodes.Callvirt, tgtProp.SetMethod, null);
                    continue;
                }

                // 对于无法处理的类型，忽略该属性（可考虑抛出异常，但为了健壮性，跳过）
            }

            // 返回目标对象（如果目标类型是值类型，需要装箱）
            il.Emit(OpCodes.Ldloc, targetLocal);
            if (targetType.IsValueType)
                il.Emit(OpCodes.Box, targetType);
            il.Emit(OpCodes.Ret);

            // 编译动态方法为委托
            return (Func<object, object>)method.CreateDelegate(typeof(Func<object, object>));
        }


        private static bool IsEnumerable(Type type) =>
            typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string);

        private static bool IsCollectionOrArray(Type type) =>
            type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));

        /// <summary>
        /// 获取可枚举类型的元素类型
        /// </summary>
        private static Type GetElementType(Type enumerableType)
        {
            if (enumerableType.IsArray)
                return enumerableType.GetElementType();

            if (enumerableType.IsGenericType && enumerableType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return enumerableType.GetGenericArguments()[0];

            // 尝试从接口中获取 IEnumerable<T>
            foreach (var iface in enumerableType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            return typeof(object);
        }

        private static bool IsComplex(Type type) =>
            !type.IsPrimitive && type != typeof(string) && !type.IsEnum && !type.IsValueType && !IsEnumerable(type);

        /// <summary>snake_case → PascalCase</summary>
        private static string SnakeToPascal(string snake)
        {
            if (!snake.Contains('_')) return snake;
            var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
        }

        /// <summary>PascalCase → snake_case</summary>
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

        /// <summary>
        /// 按优先级匹配目标属性：精确匹配 → snake↔Pascal → 忽略大小写
        /// </summary>
        private static PropertyInfo FindMatchingProperty(Type targetType, string sourcePropName)
        {
            var allProps = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToArray();

            // 1. 精确匹配
            var prop = allProps.FirstOrDefault(p => p.Name == sourcePropName);
            if (prop != null) return prop;

            // 2. 源 snake_case → 目标 PascalCase
            if (sourcePropName.Contains('_'))
            {
                var pascal = SnakeToPascal(sourcePropName);
                prop = allProps.FirstOrDefault(p => string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop;
            }

            // 3. 源 PascalCase → 目标 snake_case
            if (sourcePropName.Any(char.IsUpper))
            {
                var snake = PascalToSnake(sourcePropName);
                prop = allProps.FirstOrDefault(p => string.Equals(p.Name, snake, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return prop;
            }

            // 4. 忽略大小写兜底
            return allProps.FirstOrDefault(p => string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase));
        }

        // ---------- 辅助方法：实际映射逻辑（供 IL 调用） ----------

        /// <summary>
        /// 映射单个嵌套对象（供 Emit 调用）
        /// </summary>
        private static TTarget MapNested<TSource, TTarget>(TSource source) where TTarget : class, new()
        {
            if (source == null) return null;
            var mapper = GetOrCreateMapper(typeof(TSource), typeof(TTarget));
            return (TTarget)mapper(source);
        }

        /// <summary>
        /// 将源集合映射为 List&lt;TTarget&gt;（供 Emit 调用）
        /// </summary>
        private static List<TTarget>? MapToList<TSource, TTarget>(IEnumerable<TSource> source)
        {
            if (source == null) return null;
            var mapper = GetOrCreateMapper(typeof(TSource), typeof(TTarget));
            return source.Select(item => (TTarget)mapper(item)).ToList();
        }

        /// <summary>
        /// 将源集合映射为 TTarget[] 数组（供 Emit 调用）
        /// </summary>
        private static TTarget[] MapToArray<TSource, TTarget>(IEnumerable<TSource> source)
        {
            if (source == null) return null;
            var mapper = GetOrCreateMapper(typeof(TSource), typeof(TTarget));
            return source.Select(item => (TTarget)mapper(item)).ToArray();
        }
    }
}