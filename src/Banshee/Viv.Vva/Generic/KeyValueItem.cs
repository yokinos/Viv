using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva.Generic
{
    /// <summary>
    /// 通用键值对数据模型
    /// 用于封装任意类型的键（Key）和值（Value）组合，替代系统原生KeyValuePair的不可变限制
    /// </summary>
    /// <typeparam name="TKey">键的类型</typeparam>
    /// <typeparam name="TValue">值的类型</typeparam>
    /// <remarks>
    /// 区别于 <see cref="KeyValuePair{TKey, TValue}"/>：
    /// 1. 本类为可变类型（属性支持读写），KeyValuePair为不可变结构体；
    /// 2. 本类属性支持null值（可空类型），适配更多业务场景；
    /// 3. 提供无参构造函数，便于序列化/反序列化、反射实例化等场景
    /// </remarks>
    public class KeyValueItem<TKey, TValue>
    {
        /// <summary>
        /// 无参构造函数
        /// 适配序列化、反射、ORM等需要无参实例化的场景
        /// </summary>
        public KeyValueItem() { }

        /// <summary>
        /// 带参构造函数
        /// 快速初始化键值对数据
        /// </summary>
        /// <param name="key">键值对的键</param>
        /// <param name="value">键值对的值</param>
        public KeyValueItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>
        /// 键值对的键（支持null值）
        /// </summary>
        public TKey? Key { get; set; }

        /// <summary>
        /// 键值对的值（支持null值）
        /// </summary>
        public TValue? Value { get; set; }
    }
}