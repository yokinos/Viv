using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Any
{
    public struct SelectItem<TValue>
    {
        public SelectItem() { }

        public SelectItem(string? label, TValue? value)
        {
            Label = label;
            Value = value;
        }

        /// <summary>
        /// 下拉框的 label
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// 下拉框的 value
        /// </summary>
        public TValue? Value { get; set; }
    }
}
