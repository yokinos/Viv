using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System
{
    public class Out
    {
        public static void Println(object obj)
        {
            Console.WriteLine(Serialize(obj));
        }

        public static void PrintlnFormatJson(object obj)
        {
            var json = FormatSerialize(obj);
            Console.WriteLine(json);
        }

        public static void Println(params object[] objs)
        {
            string template = "";
            for (int i = 0; i < objs.Length; i++)
            {
                template += "{" + i + "},";
            }

            string[] args = new string[objs.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                args[i] = Serialize(objs[i]);
            }

            template = template.Substring(0, template.Length - 1);
            Console.WriteLine(string.Format(template, args));
        }

        public static void Println(object obj, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(Serialize(obj));
            Console.ResetColor();
        }

        public static void Write(object obj, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(Serialize(obj));
            Console.ResetColor();
        }

        public static void PrintlnError(object obj)
        {
            Println(obj, ConsoleColor.Red);
        }

        public static void PrintlnSuccess(object obj)
        {
            Println(obj, ConsoleColor.Green);
        }

        public static void PrintlnDebug(object obj)
        {
            Println(obj, ConsoleColor.Blue);
        }

        public static void PrintlnWarning(object obj)
        {
            Println(obj, ConsoleColor.Yellow);
        }

        public static string Serialize(object input)
        {
            return input == null ? string.Empty : input is string txt ? txt : JsonConvert.SerializeObject(input);
        }

        public static string FormatSerialize(object input)
        {
            return input == null ? string.Empty : input is string txt ? txt : JsonConvert.SerializeObject(input, Formatting.Indented);
        }
    }
}
