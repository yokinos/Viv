using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Viv.Vva
{
    /// <summary>
    /// 全局类型缓存  
    /// 不要在其他地方再静态缓存类型了 不要浪费内存
    /// </summary>
    public static class VivTypeReflectionCache
    {
        private readonly static ConcurrentDictionary<Type, List<PropertyInfo>> _typeDict = [];

        /// <summary>
        /// 获取类型T的属性列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<PropertyInfo> GetPropertieList<T>()
        {
            var type = typeof(T);
            return GetPropertieList(type);
        }

        /// <summary>
        /// 获取类型的属性列表
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static List<PropertyInfo> GetPropertieList(Type type)
        {
            if (_typeDict.TryGetValue(type, out var list))
            {
                return list;
            }

            var properties = type.GetProperties().ToList();
            _typeDict[type] = properties;
            return properties;
        }

        public static List<string> GetPropertyNameList(Type type)
        {
            var list = new List<string>();

            foreach (var property in type.GetProperties())
            {
                list.Add(property.Name);
            }

            return list;
        }
    }
}
