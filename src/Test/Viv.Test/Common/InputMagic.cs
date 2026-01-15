using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viv.Test.Common
{
    public class InputMagic
    {
        public static string GetInput(string msg, bool allowEmpty = false, ConsoleColor color = ConsoleColor.White, bool print = false)
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                Out.Println(msg, color);
            }
            string input = Console.ReadLine();
            if (!allowEmpty && !string.IsNullOrWhiteSpace(input))
            {
                Out.PrintlnError("当前输入结果不可以为空,请重新输入！");
                return GetInput(null);
            }

            if (print)
            {
                Out.Println(input, color);
            }

            return input;
        }
    }
}
