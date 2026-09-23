using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using Viv.Sandrone.Conveter;

namespace Viv.Engine
{
    /// <summary>
    /// Newtonsoft.Json 序列化配置集合
    /// </summary>
    public class JsonNetSetting
    {
        /// <summary>
        /// API响应序列化配置
        /// <list type="bullet">
        /// <item><description>输出字段首字母小写（驼峰命名）</description></item>
        /// <item><description>long类型自动序列化为字符串，防止前端精度丢失</description></item>
        /// <item><description>日期格式：yyyy-MM-dd HH:mm:ss</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings ApiResponseSettings = new()
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            ContractResolver = new VivContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        };

        /// <summary>
        /// 驼峰命名、反序列化忽略大小写配置
        /// <list type="bullet">
        /// <item><description>序列化输出首字母小写</description></item>
        /// <item><description>Newtonsoft原生反序列化对属性名默认大小写不敏感</description></item>
        /// <item><description>日期格式：yyyy-MM-dd HH:mm:ss</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings IgnoreCaseSettings = new()
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        /// <summary>
        /// 下划线命名配置（蛇形命名）
        /// <list type="bullet">
        /// <item><description>序列化输出下划线命名，例如 UserName → user_name</description></item>
        /// <item><description>字典key同样应用下划线转换</description></item>
        /// <item><description>日期格式：yyyy-MM-dd HH:mm:ss</description></item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerSettings SnakeCaseSettings = new()
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy
                {
                    ProcessDictionaryKeys = true,
                    //OverrideSpecifiedNames = true
                }
            }
        };
    }
}
