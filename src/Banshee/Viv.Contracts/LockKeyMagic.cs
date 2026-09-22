using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Viv.Contracts
{
    /// <summary>
    /// 分布式锁 Key 的唯一生成处。
    ///
    /// <see cref="Interface.IDistributedLock"/> 各方法的 key 都是 object：string 原样作 Redis Key、
    /// 前缀由调用方自己拼，其余类型在这里归一化。框架内凡是造锁 Key 的地方都从这儿走，
    /// 别在调用点各写各的前缀 —— 前缀写岔了就是两把锁，表现是「锁没生效」，而 Redis 里什么都看不出来。
    ///
    /// 两族前缀分得开，排查时 keys 出来一眼认得出是哪一族：
    /// <see cref="BusinessPrefix"/> 是通用业务锁（缓存击穿、仓储级互斥），
    /// <see cref="NanaPrefix"/> 是消息级去重锁，只有消费锁在用。
    /// </summary>
    public static class LockKeyMagic
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propCache = new();

        /// <summary>
        /// 通用业务锁前缀
        /// </summary>
        public const string BusinessPrefix = "lock:";

        /// <summary>
        /// 消息级去重锁前缀，只有消费锁在用
        /// </summary>
        public const string NanaPrefix = "nana:";

        /// <summary>
        /// 按传入顺序拼段、冒号分隔：<c>{prefix}a:b:c</c>。段数固定、格式固定的 Key 走这个。
        ///
        /// 别图省事用匿名对象去凑：<see cref="Generate(object, string)"/> 那条路按属性名的字母序拼、
        /// 下划线分隔，改个属性名或加个属性就等于换了一把锁。
        /// </summary>
        public static string Join(string prefix, params object?[] parts)
            => prefix + string.Join(':', parts);

        /// <summary>
        /// 把任意 key 归一化成 Redis Key。
        /// string 原样返回（调用方自己拼前缀，如消费锁的 nana:...），其余类型统一加 <see cref="BusinessPrefix"/>：
        /// 值类型/枚举直接 ToString，其余反射公开可读属性、按属性名排序后 _ 拼接，一个可读属性都没有就退回 ToString。
        /// </summary>
        public static string Generate(object? key) => Generate(key, BusinessPrefix);

        /// <summary>
        /// 同 <see cref="Generate(object?)"/>，但换前缀
        /// </summary>
        public static string Generate(object? key, string prefix)
        {
            if (key is string str)
                return str;

            if (key == null)
                return $"{prefix}null";

            var type = key.GetType();

            if (type.IsPrimitive || type.IsValueType || type == typeof(decimal) || type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) ||
                type.IsEnum)
            {
                return $"{prefix}{key}";
            }

            var props = _propCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .OrderBy(p => p.Name)
                .ToArray());

            if (props.Length == 0)
                return $"{prefix}{key}";

            return $"{prefix}{string.Join("_", props.Select(p => p.GetValue(key)?.ToString() ?? "null"))}";
        }
    }
}
