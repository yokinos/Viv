using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva.Generic
{
    public class KeyValueItem<TKey, TValue>
    {
        public KeyValueItem() { }

        public KeyValueItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        public TKey? Key { get; set; }

        public TValue? Value { get; set; }
    }
}
