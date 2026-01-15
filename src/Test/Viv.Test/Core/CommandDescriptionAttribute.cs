using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viv.Test.Core
{
    [AttributeUsage(AttributeTargets.Field)]
    public class CommandDescriptionAttribute : Attribute
    {
        public string Command { get; set; }

        public string Descriptrion { get; set; }

        public CommandType CommandType { get; set; }

        public CommandDescriptionAttribute(string cmd, string descriptrion, bool isSystem = false)
        {
            Command = cmd;
            Descriptrion = descriptrion;
            CommandType = isSystem ? CommandType.System : CommandType.Custom;
        }
    }
}
