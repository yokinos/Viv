using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ContractImplAttribute<T> : Attribute
    {
        public T Tag { get; set; }

        public ContractImplAttribute(T tag)
        {
            Tag = tag;
        }
    }
}
