using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using Viv.Vva.Generic;

namespace Viv.Vva.Extension
{
    public static partial class Extensions
    {
        /// <summary>
        /// [扩展方法] 将集合元素拼接为字符串
        /// </summary>
        /// <typeparam name="T">集合元素类型</typeparam>
        /// <param name="self">待拼接的集合（不能为空）</param>
        /// <param name="key">拼接分隔符，默认英文逗号","</param>
        /// <returns>拼接后的字符串，元素会调用ToString()转换为字符串</returns>
        /// <exception cref="ArgumentNullException">self为null时抛出</exception>
        /// <example>
        /// 示例：new List<int>{1,2,3}.Join("|") → "1|2|3"
        /// </example>
        public static string Join<T>([NotNull] this ICollection<T> self, string key = ",")
        {
            return string.Join(key, self);
        }

        /// <summary>
        /// [扩展方法] 判断泛型可枚举集合是否为null或空
        /// </summary>
        /// <typeparam name="T">集合元素类型</typeparam>
        /// <param name="self">待判断的可枚举集合</param>
        /// <returns>
        /// 1. self为null → true
        /// 2. self为ICollection<T> → 集合Count=0时返回true
        /// 3. 其他可枚举类型 → 无元素时返回true
        /// </returns>
        /// <remarks>
        /// 优化ICollection<T>的判断性能（直接取Count，无需遍历）；
        /// 非ICollection<T>类型会遍历判断是否有元素，大数据量需注意性能
        /// </remarks>
        [Pure]
        public static bool IsNullOrEmpty<T>([NotNullWhen(false)][AllowNull] this IEnumerable<T> self)
        {
            return self switch
            {
                null => true,
                ICollection<T> collection => collection.Count == 0,
                _ => !self.Any()
            };
        }

        /// <summary>
        /// [扩展方法] 判断DataTable是否为null或无数据行
        /// </summary>
        /// <param name="self">待判断的DataTable</param>
        /// <returns>true=self为null 或 Rows.Count=0；false=DataTable有数据行</returns>
        /// <remarks>仅判断行数量，不判断列数量</remarks>
        [Pure]
        public static bool IsNullOrEmpty([NotNullWhen(false)][AllowNull] this DataTable self)
        {
            return self == null || self.Rows.Count == 0;
        }

        /// <summary>
        /// [扩展方法] 将扁平集合生成树形结构（递归构建）
        /// </summary>
        /// <typeparam name="T">源集合元素类型</typeparam>
        /// <typeparam name="Key">主键/父键类型（需支持相等比较）</typeparam>
        /// <typeparam name="OrderbyKey">排序字段类型</typeparam>
        /// <param name="self">扁平源集合（为null时返回空）</param>
        /// <param name="rootValue">根节点的父键值（用于筛选顶级节点）</param>
        /// <param name="keySelector">获取节点主键的委托</param>
        /// <param name="parentKeySelector">获取节点父键的委托</param>
        /// <param name="orderbySelector">节点排序的委托（可选，为null时不排序）</param>
        /// <returns>树形结构的根节点集合（TreeItem<T>类型）</returns>
        /// <exception cref="ArgumentNullException">keySelector/parentKeySelector为null时抛出</exception>
        /// <remarks>
        /// 1. 递归构建树形结构，适合菜单、组织架构等层级数据；
        /// 2. 先通过ToLookup构建父键-子节点映射，提升递归查询性能；
        /// 3. 支持节点排序，排序后子节点会按指定规则排列
        /// </remarks>
        public static IEnumerable<TreeItem<T>> GenerateTree<T, Key, OrderbyKey>([AllowNull] this IEnumerable<T> list,
            Key rootValue,
            Func<T, Key> keySelector,
            Func<T, Key> parentKeySelector,
            Func<T, OrderbyKey>? orderbySelector = null)
        {
            if (list == null) yield break;
            ArgumentNullException.ThrowIfNull(keySelector);
            ArgumentNullException.ThrowIfNull(parentKeySelector);

            var sortlist = orderbySelector != null ? [.. list.OrderBy(orderbySelector)] : list.ToList();
            var lookup = sortlist.ToLookup(parentKeySelector);
            var comparer = EqualityComparer<Key>.Default;

            foreach (var root in sortlist.Where(x => comparer.Equals(rootValue, parentKeySelector(x))))
            {
                yield return BuildTree(root);
            }

            TreeItem<T> BuildTree(T node)
            {
                var key = keySelector(node);
                var children = lookup[key]
                    .Select(child => BuildTree(child))
                    .ToList();
                return new TreeItem<T>(node, children);
            }
        }

        /// <summary>
        /// [扩展方法] 实现LINQ左连接（Left Join），兼容左表为空、右表为空场景
        /// </summary>
        /// <typeparam name="TLeft">左表元素类型</typeparam>
        /// <typeparam name="TRight">右表元素类型</typeparam>
        /// <typeparam name="TKey">连接键类型</typeparam>
        /// <typeparam name="TResult">结果集元素类型</typeparam>
        /// <param name="left">左表集合（为null/空时返回空）</param>
        /// <param name="right">右表集合（为null时视为空集合）</param>
        /// <param name="leftKeySelector">左表连接键选择器</param>
        /// <param name="rightKeySelector">右表连接键选择器</param>
        /// <param name="resultSelector">结果映射委托（右表无匹配项时传入null）</param>
        /// <returns>左连接后的结果集，左表所有元素都会保留，右表无匹配则为null</returns>
        /// <remarks>
        /// 1. 底层通过ToLookup构建右表连接键映射，比原生GroupJoin+SelectMany更高效；
        /// 2. 左表为空时直接返回空，右表为空时左表元素会匹配到null；
        /// 3. 支持一对多连接，左表一条记录匹配右表多条时会返回多条结果
        /// </remarks>
        public static IEnumerable<TResult> LeftJoin<TLeft, TRight, TKey, TResult>([AllowNull] this IEnumerable<TLeft> left, IEnumerable<TRight> right,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight?, TResult> resultSelector)
        {
            #region linq实现示例（保留注释，便于理解核心逻辑）

            /*
            var leftList = left ?? Enumerable.Empty<TLeft>();
            var rightList = right ?? Enumerable.Empty<TRight>();
            return leftList.
                GroupJoin(
                    rightList,
                    leftKeySelector,
                    rightKeySelector,
                    (leftItem, rightItems) => new { LeftItem = leftItem, RightItems = rightItems.DefaultIfEmpty() }
                )
                .SelectMany(
                    group => group.RightItems,
                    (group, rightItem) => resultSelector(group.LeftItem, rightItem)
                );
            */

            #endregion

            if (left.IsNullOrEmpty()) { yield break; }
            var rightList = right ?? [];
            var rightLookup = rightList.ToLookup(rightKeySelector);
            foreach (var leftItem in left)
            {
                var leftKey = leftKeySelector(leftItem);
                var rightItems = rightLookup[leftKey];
                if (rightItems.Any())
                {
                    foreach (var rightItem in rightItems)
                    {
                        yield return resultSelector(leftItem, rightItem);
                    }
                }
                else
                {
                    yield return resultSelector(leftItem, default);
                }
            }
        }
    }
}