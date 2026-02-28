using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text;

namespace Viv.Momo
{
    public class VivMomoMagic
    {
        /// <summary>
        /// 获取表名称
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static string GetTableName<T>()
        {
            var entityType = typeof(T);
            TableAttribute? tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            return tableAttr?.Name ?? entityType.Name;
        }

    }
}
