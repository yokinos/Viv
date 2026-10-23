using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Entity.Any;

namespace Viv.Elysia.Extension
{
    public static partial class ElysiaExtensions
    {
        /// <summary>
        /// [扩展方法] List(KeyValueItem) -> List(SelectItem)
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="self"></param>
        /// <returns></returns>
        public static List<SelectItem<TValue>> ToSelectList<TValue>(this IEnumerable<KeyValueItem<string, TValue>> self)
        {
            if (self == null) return [];
            return self.Select(x => new SelectItem<TValue>(x.Key, x.Value)).ToList();
        }
    }
}
