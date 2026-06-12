using System;

namespace Viv.Log
{
    /// <summary>
    /// 空日志实现
    /// </summary>
    public class NoneLoggerImpl : ILoggerContract
    {
        public NoneLoggerImpl() { }

        public void Info(string message, params object[] args)
        {
            Console.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} {GetArgs(args)}");
        }

        public void Error(string message, Exception ex, params object[] args)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} | {ex?.Message} {GetArgs(args)}");
        }

        public void Error(string message, params object[] args)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} {GetArgs(args)}");
        }

        public void Debug(string message, params object[] args)
        {
            Console.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} {GetArgs(args)}");
        }

        public void Warning(string message, params object[] args)
        {
            Console.WriteLine($"[WARNING] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} {GetArgs(args)}");
        }

        public void Fatal(string message, params object[] args)
        {
            Console.WriteLine($"[FATAL] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} {GetArgs(args)}");
        }

        public void Fatal(string message, Exception ex, params object[] args)
        {
            Console.WriteLine($"[FATAL] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message} | {ex?.Message} {GetArgs(args)}");
        }

        private string GetArgs(object[] args)
        {
            if (args == null || args.Length == 0)
                return string.Empty;

            return "| Args: " + string.Join(", ", args);
        }
    }
}