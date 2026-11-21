using System;
using System.Collections.Generic;

namespace Viv.Entity.Any
{
    /// <summary>
    /// 下拉框选项（不可变值对象）。
    /// Label 用于界面展示，Value 承载实际业务值。
    /// </summary>
    /// <typeparam name="TValue">选项值的类型</typeparam>
    public readonly struct SelectItem<TValue> : IEquatable<SelectItem<TValue>>
    {
        /// <summary>
        /// 界面展示文本（不为 null）
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// 实际业务值
        /// </summary>
        public TValue Value { get; }

        /// <summary>
        /// 创建下拉选项。
        /// </summary>
        /// <param name="label">展示文本，不允许为 null/空白</param>
        /// <param name="value">业务值</param>
        /// <exception cref="ArgumentNullException">label 为 null</exception>
        public SelectItem(string label, TValue value)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Value = value;
        }

        public bool Equals(SelectItem<TValue> other) => string.Equals(Label, other.Label, StringComparison.Ordinal) && EqualityComparer<TValue>.Default.Equals(Value, other.Value);

        public override bool Equals(object? obj) => obj is SelectItem<TValue> o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(Label, Value);

        public static bool operator ==(SelectItem<TValue> a, SelectItem<TValue> b) => a.Equals(b);

        public static bool operator !=(SelectItem<TValue> a, SelectItem<TValue> b) => !a.Equals(b);

        public override string ToString() => $"{Label} ({Value})";

        public void Deconstruct(out string label, out TValue value)
        {
            label = Label;
            value = Value;
        }
    }
}