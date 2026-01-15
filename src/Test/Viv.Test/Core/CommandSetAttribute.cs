using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Viv.Test.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class CommandSetAttribute : Attribute
    {
        public CommandSetAttribute(Command cmd)
        {
            FieldInfo field = cmd.GetType().GetField(cmd.ToString());
            if (field.IsDefined(typeof(CommandDescriptionAttribute), true))
            {
                CommandDescriptionAttribute attribute = (CommandDescriptionAttribute)field.GetCustomAttribute(typeof(CommandDescriptionAttribute));
                Assembly = new CommandModel(attribute.Command, attribute.CommandType, attribute.Descriptrion);
            }
        }

        public CommandModel Assembly { get; set; }
    }
}
