using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Enums;

namespace Viv.Engine.Options
{
    public class EnvOptions
    {
        public VivEnv Env { get; set; }

        public string? ServiceName { get; set; }

        public int MachineId { get; set; }

        public VivServiceType ServiceType { get; set; }

        /// <summary>
        /// 内部请求共享签名密钥（x-request-token HMAC-SHA256）。
        /// 网关与所有服务必须配置同一个值；缺省时回落到 TokenOptions.SecretKey（向后兼容）。
        /// </summary>
        public string? InternalToken { get; set; }
    }
}
