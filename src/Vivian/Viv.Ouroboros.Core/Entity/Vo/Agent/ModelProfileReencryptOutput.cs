using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 档位密钥重加密结果：只回数量与档位键，绝不回密钥或密文
    /// </summary>
    public class ModelProfileReencryptOutput
    {
        /// <summary>
        /// 参与处理的行数（有密文的那些）
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 成功重加密并写回的行数
        /// </summary>
        public int Reencrypted { get; set; }

        /// <summary>
        /// 处理前仍是旧钥匙（InternalToken）加密的行数 —— 迁移前跑一次看它，迁移后它该是 0
        /// </summary>
        public int LegacyBefore { get; set; }

        /// <summary>
        /// 解不开或写库失败的行（档位键）。这些行没被动过，需要人工确认密钥来源
        /// </summary>
        public List<string> FailedKeys { get; set; } = [];

        /// <summary>
        /// 是否已配置专用主钥匙。false = 仍在回退态，重加密只是换一次密文，不解决密钥解耦
        /// </summary>
        public bool DedicatedKeyConfigured { get; set; }
    }
}
