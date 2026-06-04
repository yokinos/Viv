using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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
        private static ReadOnlySpan<string> Units => new[] { "Byte", "KB", "MB", "GB", "TB", "PB" };
        private const string Numbers = "0123456789";
        private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
        private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private static partial class RegularConst
        {
            [GeneratedRegex(@"^[\w-+]+(\.[\w-+]+)*@[\w-]+(\.[\w-]+)+$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "zh-CN")]
            public static partial Regex EmailRegex();

            [GeneratedRegex(@"^1[3-9]\d{9}$", RegexOptions.Compiled)]
            public static partial Regex MobileRegex();

            [GeneratedRegex(@"^(\d{18}|\d{17}[\dXx])$")]
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

            ReadOnlySpan<char> span = input.AsSpan();
            if (span.Length != 18) return false;

            // 省份代码校验
            ReadOnlySpan<int> provinceCodes = [11, 12, 13, 14, 15, 21, 22, 23, 31, 32, 33, 34, 35, 36, 37, 41, 42, 43, 44, 45, 46, 50, 51, 52, 53, 54, 61, 62, 63, 64, 65, 71, 81, 82, 91];
            if (!int.TryParse(span.Slice(0, 2), out int provinceCode) ||
                !provinceCodes.Contains(provinceCode))
                return false;

            // 生日校验
            if (!DateTime.TryParseExact(span.Slice(6, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _))
                return false;

            // 校验位计算
            ReadOnlySpan<int> weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
            int total = 0;
            for (int i = 0; i < 17; i++)
            {
                char c = span[i];
                if (c < '0' || c > '9') return false;   // 确保数字
                total += (c - '0') * weights[i];
            }

            ReadOnlySpan<char> checkCodes = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2'];
            return checkCodes[total % 11] == char.ToUpper(span[17]);
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

            int unitIndex = 0;
            double size = fileSize;

            while (size >= 1024 && unitIndex < Units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {Units[unitIndex]}";
        }



        /// <summary>
        /// 生成安全的随机字符串
        /// </summary>
        public static string GenerateSecureString(int length, bool useNumber = true, bool useLower = true, bool useUpper = false, string customChars = "")
        {
            if (length <= 0) return string.Empty;

            var charPool = BuildCharPool(useNumber, useLower, useUpper, customChars);
            if (charPool.Length == 0)
                throw new ArgumentException("至少需要指定一种有效的字符集");

            var result = new StringBuilder(length);
            // 使用加密级别的随机数生成器
            using var rng = RandomNumberGenerator.Create();

            byte[] randomBytes = new byte[4];
            for (int i = 0; i < length; i++)
            {
                rng.GetBytes(randomBytes);
                // 将随机字节映射到字符池范围内，保证均匀分布
                uint randomUint = BitConverter.ToUInt32(randomBytes, 0);
                int index = (int)(randomUint % (uint)charPool.Length);
                result.Append(charPool[index]);
            }

            return result.ToString();
        }

        /// <summary>
        /// 生成普通的随机字符串
        /// </summary>
        public static string GenerateFastString(int length, bool useNumber = true, bool useLower = true, bool useUpper = false, string customChars = "")
        {
            if (length <= 0) return string.Empty;

            var charPool = BuildCharPool(useNumber, useLower, useUpper, customChars);
            if (charPool.Length == 0)
                throw new ArgumentException("至少需要指定一种有效的字符集");

            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                int index = RandomMagic.Next(0, charPool.Length);
                result.Append(charPool[index]);
            }

            return result.ToString();
        }

        private static string BuildCharPool(bool useNumber, bool useLower, bool useUpper, string customChars)
        {
            var sb = new StringBuilder(62 + (customChars?.Length ?? 0));
            if (useNumber) sb.Append(Numbers);
            if (useLower) sb.Append(LowerChars);
            if (useUpper) sb.Append(UpperChars);
            if (!string.IsNullOrEmpty(customChars)) sb.Append(customChars);
            return sb.ToString();
        }
    }
}