using System;

namespace Viv.Delusion.Generic
{
    /// <summary>
    /// 通用键值对数据模型。
    /// 用于封装任意类型的键（Key）与值（Value）组合，支持可变赋值与 null 值。
    /// </summary>
    /// <typeparam name="TKey">键的类型</typeparam>
    /// <typeparam name="TValue">值的类型</typeparam>
    /// <remarks>
    /// 与 <see cref="KeyValuePair{TKey, TValue}"/> 的区别：
    /// 1. 引用类型，可空、可继承（若需）；
    /// 2. 属性可读写，适合 DTO / 序列化 / 配置绑定场景；
    /// 3. 允许 null 值（引用类型 TValue 或 Nullable&lt;TValue&gt;）。
    /// 注意：本类型**可变**，不适合作为 Dictionary/HashSet 的键。
    /// </remarks>
    public sealed class KeyValueItem<TKey, TValue> : IEquatable<KeyValueItem<TKey, TValue>>
    {
        public KeyValueItem() { }

        public KeyValueItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>
        /// 键值对的键（允许 null）
        /// </summary>
        public TKey? Key { get; set; }

        /// <summary>
        /// 键值对的值（允许 null）
        /// </summary>
        public TValue? Value { get; set; }

        public bool Equals(KeyValueItem<TKey, TValue>? other)
        {
            if (other is null) return false;
            return EqualityComparer<TKey?>.Default.Equals(Key, other.Key) && EqualityComparer<TValue?>.Default.Equals(Value, other.Value);
        }

        public override bool Equals(object? obj) => Equals(obj as KeyValueItem<TKey, TValue>);

        public override int GetHashCode() => HashCode.Combine(Key is null ? 0 : EqualityComparer<TKey?>.Default.GetHashCode(Key), Value is null ? 0 : EqualityComparer<TValue?>.Default.GetHashCode(Value));

        public override string ToString() => $"[{Key}, {Value}]";

        public void Deconstruct(out TKey? key, out TValue? value)
        {
            key = Key;
            value = Value;
        }
    }
}