using Newtonsoft.Json;
using Spectre.Console;

namespace Viv.Cli
{
    public static class Out
    {
        public static void Println(object obj)
        {
            AnsiConsole.WriteLine(Serialize(obj));
        }

        public static void PrintlnFormatJson(object obj)
        {
            var json = FormatSerialize(obj);
            AnsiConsole.Write(
                new Panel(new Text(json))
                    .Header("JSON")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Grey));
        }

        public static void Println(params object[] objs)
        {
            var parts = new string[objs.Length];
            for (int i = 0; i < objs.Length; i++)
                parts[i] = Serialize(objs[i]);
            AnsiConsole.WriteLine(string.Join(", ", parts));
        }

        public static void Println(object obj, Color color)
        {
            AnsiConsole.MarkupLine($"[{color.ToMarkup()}]{Escape(obj)}[/]");
        }

        public static void PrintlnError(object obj)
        {
            AnsiConsole.MarkupLine($"[red]{Escape(obj)}[/]");
        }

        public static void PrintlnSuccess(object obj)
        {
            AnsiConsole.MarkupLine($"[green]{Escape(obj)}[/]");
        }

        public static void PrintlnWarning(object obj)
        {
            AnsiConsole.MarkupLine($"[yellow]{Escape(obj)}[/]");
        }

        public static void PrintlnInfo(object obj)
        {
            AnsiConsole.MarkupLine($"[blue]{Escape(obj)}[/]");
        }

        public static string Serialize(object input)
        {
            return input == null ? string.Empty
                 : input is string txt ? txt
                 : JsonConvert.SerializeObject(input);
        }

        public static string FormatSerialize(object input)
        {
            return input == null ? string.Empty
                 : input is string txt ? txt
                 : JsonConvert.SerializeObject(input, Formatting.Indented);
        }

        private static string Escape(object obj)
        {
            return (Serialize(obj) ?? string.Empty)
                .Replace("[", "[[").Replace("]", "]]");
        }
    }
}
