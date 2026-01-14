using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Text;
using Viv.Vva.Generic;

namespace Viv.Vva.Extension
{
    public static partial class Extensions
    {
        public static string Join<T>([NotNull] this ICollection<T> self, string key = ",")
        {
            return string.Join(key, self);
        }

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

        [Pure]
        public static bool IsNullOrEmpty([NotNullWhen(false)][AllowNull] this DataTable self)
        {
            return self == null || self.Rows.Count == 0;
        }

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

        public static IEnumerable<TResult> LeftJoin<TLeft, TRight, TKey, TResult>([AllowNull] this IEnumerable<TLeft> left, IEnumerable<TRight> right,
            Func<TLeft, TKey> leftKeySelector,
            Func<TRight, TKey> rightKeySelector,
            Func<TLeft, TRight?, TResult> resultSelector)
        {
            #region linq实现示例

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
