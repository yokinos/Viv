using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Viv.Log
{
    public class ExceptionAnalyzer
    {
        /// <summary>
        /// 解析异常为完整字符串（包含消息、堆栈、内部异常、数据）
        /// </summary>
        /// <param name="exception">待解析的异常（可为null）</param>
        /// <param name="includeData">是否包含Exception.Data中的自定义数据（默认true）</param>
        /// <param name="includeStackTrace">是否包含堆栈信息（默认true）</param>
        /// <returns>异常完整信息字符串（null返回空字符串）</returns>
        public static string Parse(Exception? exception, bool includeData = true, bool includeStackTrace = true)
        {
            if (exception == null)
                return string.Empty;

            var sb = new StringBuilder();
            var depth = 0;

            ParseRecursive(exception, sb, ref depth, includeData, includeStackTrace);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 递归解析异常（处理嵌套内部异常）
        /// </summary>
        private static void ParseRecursive(Exception ex, StringBuilder sb, ref int depth, bool includeData, bool includeStackTrace)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}[异常{depth + 1}] 类型：{ex.GetType().FullName}");
            sb.AppendLine($"{indent}消息：{ex.Message}");

            if (includeStackTrace && !string.IsNullOrEmpty(ex.StackTrace))
                sb.AppendLine($"{indent}堆栈：{ex.StackTrace}");

            if (includeData && ex.Data.Count > 0)
            {
                sb.AppendLine($"{indent}自定义数据：");
                foreach (DictionaryEntry entry in ex.Data)
                {
                    sb.AppendLine($"{indent}  {entry.Key} = {entry.Value ?? "null"}");
                }
            }

            if (ex.InnerException != null)
            {
                depth++;
                sb.AppendLine($"{indent}--- 内部异常 ---");
                ParseRecursive(ex.InnerException, sb, ref depth, includeData, includeStackTrace);
                depth--;
            }
        }

        /// <summary>
        /// 快速解析异常（仅保留核心信息：消息+内部异常消息）
        /// </summary>
        /// <param name="exception">待解析的异常</param>
        /// <returns>精简的异常信息</returns>
        public static string ParseSimple(Exception? exception)
        {
            if (exception == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(exception.Message);

            var innerEx = exception.InnerException;
            while (innerEx != null)
            {
                sb.Append($" | 内部异常：{innerEx.Message}");
                innerEx = innerEx.InnerException;
            }

            return sb.ToString();
        }
    }
}
