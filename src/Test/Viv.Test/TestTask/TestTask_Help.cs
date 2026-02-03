using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Viv.Test.Common;
using Viv.Test.Core;

namespace Viv.Test.TestTask
{
    /// <summary>
    /// 帮助指令测试任务 - 输出所有指令的帮助说明
    /// </summary>
    [CommandSet(Command.Help)]
    public class TestTask_Help : ITestTask
    {
        // 静态只读帮助文本 - 延迟初始化且仅初始化一次
        private static readonly Lazy<string> _lazyHelpText = new Lazy<string>(BuildHelpText);

        /// <summary>
        /// 启动帮助任务，输出格式化帮助文本
        /// </summary>
        public Task StartAsync()
        {
            Console.WriteLine(_lazyHelpText.Value);
            return Task.CompletedTask;
        }

        #region 核心构建方法

        /// <summary>
        /// 构建格式化的帮助文本
        /// </summary>
        private static string BuildHelpText()
        {
            const int TitleBaseLength = 4;       // 标题基础长度
            const int MaxColumnWhitespace = 20;  // 列最大空白填充长度
            const int DivisionLineLength = 80;   // 分隔线长度
            const char DivisionLineChar = '-';   // 分隔线字符
            const string ColumnSeparator = " ";  // 列默认分隔符

            string defaultWhitespace = CreateRepeatedString(MaxColumnWhitespace, ColumnSeparator);
            string divisionLine = CreateRepeatedString(DivisionLineLength, DivisionLineChar.ToString());

            var builder = new StringBuilder();
            builder.AppendLine(divisionLine);
            builder.AppendLine($"指令{defaultWhitespace}类型{defaultWhitespace}描述");
            builder.AppendLine(divisionLine);

            if (typeof(Command).GetField(Command.Exit.ToString()) is FieldInfo exitField)
            {
                var exitDescAttr = exitField.GetCustomAttribute<CommandDescriptionAttribute>();
                if (exitDescAttr != null)
                {
                    builder.AppendLine(FormatCommandLine(
                        exitDescAttr.Command,
                        exitDescAttr.CommandType.ToString(),
                        exitDescAttr.Descriptrion,
                        MaxColumnWhitespace,
                        TitleBaseLength));
                }
            }

            var businessCommands = ConsoleContext.GetCmdAssemblys().OrderBy(cmd => cmd.CommandType); // 空值保护
            foreach (var cmd in businessCommands)
            {
                builder.AppendLine(FormatCommandLine(
                    cmd.Command,
                    cmd.CommandType.ToString(),
                    cmd.Description,
                    MaxColumnWhitespace,
                    TitleBaseLength));
            }

            builder.AppendLine(divisionLine);
            return builder.ToString();
        }

        /// <summary>
        /// 生成重复指定次数的字符串
        /// </summary>
        /// <param name="repeatCount">重复次数</param>
        /// <param name="repeatText">要重复的文本（默认空格）</param>
        private static string CreateRepeatedString(int repeatCount, string repeatText = " ")
        {
            if (repeatCount <= 0) return string.Empty;
            return string.Concat(Enumerable.Repeat(repeatText, repeatCount));
        }

        /// <summary>
        /// 格式化单条指令行的输出
        /// </summary>
        /// <param name="command">指令名</param>
        /// <param name="cmdType">指令类型</param>
        /// <param name="description">指令描述</param>
        /// <param name="maxWhitespace">列最大空白长度</param>
        /// <param name="baseTitleLength">标题基础长度</param>
        private static string FormatCommandLine(string command, string cmdType, string description, int maxWhitespace, int baseTitleLength)
        {
            int cmdNameLength = command?.Length ?? 0;
            int typeLength = cmdType?.Length ?? 6;
            string cmdWhitespace = CreateRepeatedString(CalculateWhitespaceLength(cmdNameLength, baseTitleLength, maxWhitespace));
            string typeWhitespace = CreateRepeatedString(CalculateWhitespaceLength(typeLength, baseTitleLength, maxWhitespace));

            return $"{command}{cmdWhitespace}{cmdType}{typeWhitespace}{description}";
        }

        /// <summary>
        /// 计算列需要填充的空白长度
        /// </summary>
        private static int CalculateWhitespaceLength(int currentLength, int baseLength, int maxWhitespace)
        {
            return currentLength > baseLength
                ? maxWhitespace - (currentLength - baseLength)
                : maxWhitespace + (baseLength - currentLength);
        }

        #endregion
    }
}