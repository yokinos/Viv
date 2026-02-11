using System;
using System.Collections.Generic;
using System.Text;

#nullable disable
namespace Viv.Vva.Generic
{
    /// <summary>
    /// 通用树形节点数据模型
    /// </summary>
    /// <typeparam name="T">节点承载的数据类型</typeparam>
    /// <remarks>
    /// 1. 每个节点包含自身数据（Node）和子节点集合（Children），支持无限层级嵌套；
    /// 2. 配合Extensions.GenerateTree扩展方法使用，可快速将扁平集合转换为树形结构
    /// </remarks>
    public class TreeItem<T>
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public TreeItem() { }

        /// <summary>
        /// 带参构造函数
        /// </summary>
        /// <param name="node">当前节点的业务数据</param>
        /// <param name="children">当前节点的子节点集合（可为空，表示无下级节点）</param>
        public TreeItem(T node, IEnumerable<TreeItem<T>> children)
        {
            Node = node;
            Children = children;
        }

        /// <summary>
        /// 当前节点承载的数据
        /// </summary>
        public T Node { get; set; }

        /// <summary>
        /// 当前节点的子节点集合
        /// 为空时表示当前节点是叶子节点（无下级）
        /// </summary>
        public IEnumerable<TreeItem<T>> Children { get; set; }
    }
}