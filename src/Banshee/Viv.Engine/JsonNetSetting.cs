using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Sandrone.Conveter;

namespace Viv.Engine
{
    public class JsonNetSetting
    {
        /// <summary>
        /// Api 返回Json序列化设置
        /// </summary>
        public static readonly JsonSerializerSettings ApiResponseSettings = new()
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            ContractResolver = new VivContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        };
    }
}
