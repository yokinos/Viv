using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using Viv.Sandrone.Conveter;

namespace Viv.Engine
{
    /// <summary>
    /// Newtonsoft.Json 序列化配置集合。
    /// 时间输出统一走 <see cref="ApplyDateTimeRule"/>。
    /// </summary>
    public class JsonNetSetting
    {
        /// <summary>
        /// 统一的日期时间序列化规则：ISO 8601 + UTC，如 <c>2026-11-12T08:00:00Z</c>。
        ///
        /// <list type="bullet">
        /// <item><description><c>IsoDateFormat</c>：输出带 <c>T</c> 与 <c>Z</c> 的标准格式，前端 <c>new Date()</c> 可直接解析。</description></item>
        /// <item><description><c>DateTimeZoneHandling.Utc</c>：<c>Unspecified</c> / <c>Local</c> 一律按 UTC 输出，结尾必有 <c>Z</c>。</description></item>
        /// <item><description><c>DateTimeOffset</c> 不受影响，本身输出带偏移的 ISO（<c>+00:00</c>）。</description></item>
        /// <item><description>不要设置 <c>DateFormatString</c>：自定义格式会丢掉时区信息。</description></item>
        /// </list>
        /// </summary>
        public static void ApplyDateTimeRule(JsonSerializerSettings settings)
        {
            settings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            settings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        }

        /// <summary>
        /// 建一个套用了统一时间规则的配置。
        /// </summary>
        private static JsonSerializerSettings CreateSettings(IContractResolver resolver)
        {
            var settings = new JsonSerializerSettings { ContractResolver = resolver };
            ApplyDateTimeRule(settings);
            return settings;
        }

        /// <summary>
        /// API响应序列化配置
        /// <list type="bullet">
        /// <item><description>输出字段首字母小写（驼峰命名）</description></item>
        /// <item><description>long类型自动序列化为字符串，防止前端精度丢失</description></item>
        /// <item><description>日期：ISO 8601 + UTC，如 2026-11-12T08:00:00Z</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings ApiResponseSettings =
            CreateSettings(new VivContractResolver { NamingStrategy = new CamelCaseNamingStrategy() });

        /// <summary>
        /// 驼峰命名、反序列化忽略大小写配置
        /// <list type="bullet">
        /// <item><description>序列化输出首字母小写</description></item>
        /// <item><description>Newtonsoft原生反序列化对属性名默认大小写不敏感</description></item>
        /// <item><description>日期：ISO 8601 + UTC，如 2026-11-12T08:00:00Z</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings IgnoreCaseSettings =
            CreateSettings(new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() });

        /// <summary>
        /// 下划线命名配置（蛇形命名）
        /// <list type="bullet">
        /// <item><description>序列化输出下划线命名，例如 UserName → user_name</description></item>
        /// <item><description>字典key同样应用下划线转换</description></item>
        /// <item><description>日期：ISO 8601 + UTC，如 2026-11-12T08:00:00Z</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings SnakeCaseSettings =
            CreateSettings(new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy
                {
                    ProcessDictionaryKeys = true,
                    //OverrideSpecifiedNames = true
                }
            });
    }
}
