using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Viv.Elysia.Attributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class EnumNameAttribute : Attribute
    {
        public EnumNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }   
}
