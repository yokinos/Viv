using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class QueueSetAttribute : Attribute
    {
        public QueueSetAttribute()
        {

        }
    }
}
