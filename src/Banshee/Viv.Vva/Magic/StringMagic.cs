using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Viv.Vva.Magic
{
    /// <summary>
    /// 字符串处理工具类（纯静态方法，非扩展方法）
    /// </summary>
    public static partial class StringMagic
    {
        private static partial class RegularConst
        {
            [GeneratedRegex(@"^[\w-+]+(\.[\w-+]+)*@[\w-]+(\.[\w-]+)+$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "zh-CN")]
            public static partial Regex EmailRegex();

            [GeneratedRegex(@"^1[3-9]\d{9}$", RegexOptions.Compiled)]
            public static partial Regex MobileRegex();

            [GeneratedRegex(@"(^\d{18}$)|(^\d{17}(\d|X|x)$)", RegexOptions.Compiled)]
            public static partial Regex IDCardRegex();

            [GeneratedRegex(@"^((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)$", RegexOptions.Compiled)]
            public static partial Regex IPV4AddressRegex();

            [GeneratedRegex(@"^\d+$", RegexOptions.Compiled)]
            public static partial Regex NumberRegex();

            [GeneratedRegex(@"^[0-9]*\.?[0-9]+$|^[0-9]+\.?[0-9]*$", RegexOptions.Compiled)]
            public static partial Regex DecimalRegex();

            [GeneratedRegex(@"^[\u4e00-\u9fa5]+$", RegexOptions.Compiled)]
            public static partial Regex ChineseRegex();

            [GeneratedRegex(@"^[A-Za-z]+$", RegexOptions.Compiled)]
            public static partial Regex EnglishRegex();

            /// <summary>
            /// 通用正则校验方法
            /// </summary>
            public static bool RegexCheck(string input, Regex regex)
            {
                if (input == null) return false;
                try
                {
                    return regex.IsMatch(input);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 验证字符串是否是邮箱
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsEmail(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.EmailRegex());
        }

        /// <summary>
        /// 验证字符串是否是11位移动电话号码
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsMobile(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.MobileRegex());
        }

        /// <summary>
        /// 验证字符串是否是IPV4地址
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsIPV4Address(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.IPV4AddressRegex());
        }

        /// <summary>
        /// 验证字符串是否是纯数字
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsNumber(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.NumberRegex());
        }

        /// <summary>
        /// 验证字符串是否是浮点型数字
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsDecimal(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.DecimalRegex());
        }

        /// <summary>
        /// 验证字符串是否包含中文
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsChinese(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.ChineseRegex());
        }

        /// <summary>
        /// 验证字符串是否是纯英文
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsEnglish(string input)
        {
            return RegularConst.RegexCheck(input, RegularConst.EnglishRegex());
        }

        /// <summary>
        /// 验证字符串是否是18位二代居民身份证（含合法性校验）
        /// </summary>
        /// <param name="input">待校验的字符串</param>
        public static bool IsIDCard(string input)
        {
            if (!RegularConst.RegexCheck(input, RegularConst.IDCardRegex()))
                return false;

            // 省份代码校验
            var provinceCodes = new HashSet<string>
            {
                "11","12","13","14","15","21","22","23","31","32","33","34","35","36","37",
                "41","42","43","44","45","46","50","51","52","53","54","61","62","63","64",
                "65","71","81","82","91"
            };
            if (!provinceCodes.Contains(input[..2]))
                return false;

            // 生日合法性校验
            if (!DateTime.TryParseExact(input.Substring(6, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
                return false;

            // 校验位计算
            char[] checkCodes = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2'];
            int[] weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
            int total = 0;
            for (int i = 0; i < 17; i++)
            {
                if (!int.TryParse(input[i].ToString(), out int num))
                    return false;
                total += num * weights[i];
            }

            return checkCodes[total % 11] == char.ToUpper(input[17]);
        }

        /// <summary>
        /// 使用UTF-8对Url进行编码
        /// </summary>
        /// <param name="input">待编码的字符串</param>
        public static string UrlEncode(string input)
        {
            return input == null ? string.Empty : HttpUtility.UrlEncode(input, Encoding.UTF8);
        }

        /// <summary>
        /// 使用UTF-8对Url进行解码
        /// </summary>
        /// <param name="input">待解码的字符串</param>
        public static string UrlDecode(string input)
        {
            return input == null ? string.Empty : HttpUtility.UrlDecode(input, Encoding.UTF8);
        }

        /// <summary>
        /// 格式化JSON字符串（默认缩进4个空格）
        /// </summary>
        /// <param name="input">原始JSON字符串</param>
        /// <param name="indent">缩进空格数</param>
        public static string JsonFormat(string? input, int indent = 4)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            try
            {
                var token = JToken.Parse(input);
                return token.ToString(Newtonsoft.Json.Formatting.Indented)
                            .Replace("\r\n", Environment.NewLine)
                            .Replace("  ", new string(' ', indent));
            }
            catch
            {
                return input;
            }
        }

        /// <summary>
        /// 首字母小写
        /// </summary>
        /// <param name="input">待转换的字符串</param>
        public static string FirstLowerCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Length == 1 ? input.ToLower() : $"{char.ToLower(input[0])}{input[1..]}";
        }

        /// <summary>
        /// 首字母大写
        /// </summary>
        /// <param name="input">待转换的字符串</param>
        public static string FirstUpperCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Length == 1 ? input.ToUpper() : $"{char.ToUpper(input[0])}{input[1..]}";
        }

        /// <summary>
        /// 分隔驼峰命名的词组（如 UserName → user-name）
        /// </summary>
        /// <param name="input">驼峰命名字符串</param>
        /// <param name="separator">分隔符，默认使用"-"分隔</param>
        public static string SplitCamelCase(string input, char separator = '-')
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var pattern = @"([A-Z])(?=[a-z])|(?<=[a-z])([A-Z]|[0-9]+)";
            return Regex.Replace(input, pattern, $"{separator}$1$2").TrimStart(separator).ToLower();
        }

        /// <summary>
        /// 移除末尾指定字符串（忽略大小写）
        /// </summary>
        /// <param name="input">原始字符串</param>
        /// <param name="removeValue">要移除的字符串</param>
        public static string RemoveEnd(string input, string removeValue)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            if (string.IsNullOrWhiteSpace(removeValue)) return input;

            return input.EndsWith(removeValue, StringComparison.OrdinalIgnoreCase)
                ? input[..^removeValue.Length]
                : input;
        }

        /// <summary>
        /// 移除起始指定字符串（忽略大小写）
        /// </summary>
        /// <param name="input">原始字符串</param>
        /// <param name="removeValue">要移除的字符串</param>
        public static string RemoveStart(string input, string removeValue)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            if (string.IsNullOrWhiteSpace(removeValue)) return input;

            return input.StartsWith(removeValue, StringComparison.OrdinalIgnoreCase)
                ? input[removeValue.Length..]
                : input;
        }

        /// <summary>
        /// 将数字转换为中文大写金额
        /// </summary>
        /// <param name="amount">待转换的金额</param>
        public static string ToChineseAmount(decimal amount)
        {
            if (amount == 0) return "零元整";

            var amountStr = amount.ToString("#L#E#D#C#K#E#D#C#J#E#D#C#I#E#D#C#H#E#D#C#G#E#D#C#F#E#D#C#.0B0A");
            amountStr = amountStr.Replace("0B0A", "@");
            var regexResult = Regex.Replace(amountStr, @"((?<=-|^)[^1-9]*)|((?'z'0)[0A-E]*((?=[1-9])|(?'-z'(?=[F-L\.]|$))))|((?'b'[F-L])(?'z'0)[0A-L]*((?=[1-9])|(?'-z'(?=[\.]|$))))", "${b}${z}");
            var result = Regex.Replace(regexResult, ".", m => "负元空零壹贰叁肆伍陆柒捌玖空空空空空空整分角拾佰仟万亿兆京垓秭穰"[m.Value[0] - '-'].ToString());
            return result.Replace("空", "");
        }

        /// <summary>
        /// 将文件大小格式化为易读字符串（如 2.00 MB、1.50 GB）
        /// </summary>
        /// <param name="fileSize">文件大小（字节）</param>
        public static string ToReadableFileSize(long fileSize)
        {
            if (fileSize < 0) return "0.00 Byte";

            string[] units = ["Byte", "KB", "MB", "GB", "TB", "PB"];
            int unitIndex = 0;
            double size = fileSize;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }

        /// <summary>
        /// 生成随机字符串
        /// </summary>
        /// <param name="length">字符串长度</param>
        /// <param name="useNumber">是否使用数字（默认true）</param>
        /// <param name="useLower">是否使用小写字母（默认true）</param>
        /// <param name="useUpper">是否使用大写字母（默认false）</param>
        /// <param name="customChars">自定义字符（可选）</param>
        /// <exception cref="ArgumentException">无有效字符集时抛出</exception>
        public static string GenerateRandomString(int length, bool useNumber = true, bool useLower = true, bool useUpper = false, string customChars = "")
        {
            if (length <= 0) return string.Empty;

            var charPool = new StringBuilder();
            if (useNumber) charPool.Append("0123456789");
            if (useLower) charPool.Append("abcdefghijklmnopqrstuvwxyz");
            if (useUpper) charPool.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
            if (!string.IsNullOrEmpty(customChars)) charPool.Append(customChars);

            if (charPool.Length == 0)
                throw new ArgumentException("至少需要指定一种字符集（数字/小写/大写/自定义）", nameof(customChars));

            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                int index = RandomMagic.Next(0, charPool.Length);
                result.Append(charPool[index]);
            }

            return result.ToString();
        }
    }
}