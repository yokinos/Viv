using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Entity.Any;
using Viv.Entity.Converters;
using Viv.Entity.Database.Apex;
using Viv.Entity.Vue;

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

        /// <summary>
        ///  [扩展方法] 生成 vue router 列表
        /// </summary>
        /// <param name="menus"></param>
        /// <param name="subPages"></param>
        /// <returns></returns>
        public static List<RouteItem> ToVueRouter(this List<AtMenu> menus, List<AtMenuSubPage>? subPages = null)
        {
            return VueRouterConverter.ToVueRouter(menus, subPages);
        }
    }
}
