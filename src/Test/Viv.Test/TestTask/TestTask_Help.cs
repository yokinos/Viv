using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viv.Test.Common;
using Viv.Test.Core;
using Viv.Vva.Magic;

namespace Viv.Test.TestTask
{
    [CommandSet(Command.Help)]
    public class TestTask_Help : ITestTask
    {
        private static string _helpText = null;

        public async Task StartAsync()
        {
            if (string.IsNullOrEmpty(_helpText))
            {
                int titleLength = 4;
                int maxLength = 20;
                int divisionLeght = 80;

                string defaultWhiteSpace = GetString(maxLength);
                string division = GetString(divisionLeght, "-");

                StringBuilder builder = new StringBuilder();

                builder.AppendLine(division);
                builder.AppendLine($"指令{defaultWhiteSpace}类型{defaultWhiteSpace}描述");
                builder.AppendLine(division);

                var exitInfo = ReflectionMagic.GetAttribute<CommandDescriptionAttribute>(typeof(Command).GetField(Command.Exit.ToString()));

                builder.AppendLine($"{exitInfo.Command}{GetWhiteSpace(maxLength, 4, titleLength)}{exitInfo.CommandType}{GetWhiteSpace(maxLength, 6, titleLength)}{exitInfo.Descriptrion}");

                var cmds = ConsoleContext.GetCmdAssemblys();
                cmds = cmds.OrderBy(x => x.CommandType).ToList();

                foreach (var cmd in cmds)
                {
                    builder.Append($"{cmd.Command}{GetWhiteSpace(maxLength, cmd.Command.ToString().Length, titleLength)}{cmd.CommandType}{GetWhiteSpace(maxLength, 6, titleLength)}{cmd.Description}\r\n");
                }

                builder.AppendLine(division);
                _helpText = builder.ToString();
            }

            Console.WriteLine(_helpText);
            await Task.CompletedTask;
        }

        private static string GetString(int length, string text = " ")
        {
            string whiteSpace = string.Empty;
            for (int i = 0; i < length; i++)
            {
                whiteSpace += text;
            }
            return whiteSpace;
        }

        private static string GetWhiteSpace(int maxLength, int nowLength, int oldLength = 4)
        {
            if (nowLength > oldLength)
            {
                return GetString(maxLength - (nowLength - oldLength));
            }
            else if (nowLength == oldLength)
            {
                return GetString(maxLength);
            }
            else
            {
                return GetString(maxLength + (oldLength - nowLength));
            }
        }
    }
}
