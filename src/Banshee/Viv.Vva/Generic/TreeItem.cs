using System;
using System.Collections.Generic;
using System.Text;

#nullable disable
namespace Viv.Vva.Generic
{
    public class TreeItem<T>
    {
        public TreeItem() { }

        public TreeItem(T node, IEnumerable<TreeItem<T>> children)
        {
            Node = node;
            Children = children;
        }

        /// <summary>
        /// 当前节点
        /// </summary>
        public T Node { get; set; }

        /// <summary>
        /// 子节点
        /// </summary>
        public IEnumerable<TreeItem<T>> Children { get; set; }
    }
}
