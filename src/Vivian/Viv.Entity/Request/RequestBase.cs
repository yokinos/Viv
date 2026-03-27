using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Viv.Elysia;
using Viv.Elysia.Base;
using Viv.Elysia.Validator;
using Viv.Vva.Magic;

namespace Viv.Entity.Request
{
    /// <summary>
    /// API 请求基类（统一公共参数 + 自动验签 + 自动校验）
    /// </summary>
    [Serializable]
    public class RequestBase : IApiRequest
    {
        /// <summary>
        /// [必传]客户端AppId
        /// </summary>
        [Required]
        [Display(Name = "客户端AppId")]
        [Range(1, long.MaxValue)]
        public long AppId { get; set; }

        /// <summary>
        /// [必传]服务器内部版本号
        /// </summary>
        [Required]
        [Display(Name = "服务器内部版本号")]
        [Range(1, int.MaxValue)]
        public int Version { get; set; }

        /// <summary>
        /// [必传]时间戳(秒数)
        /// </summary>
        [Required]
        [Display(Name = "时间戳")]
        [Range(1000000000, long.MaxValue)]
        public long Timestamp { get; set; }

        /// <summary>
        ///[必传]签名字段
        /// </summary>
        [Required]
        [Display(Name = "签名字段")]
        [StringLength(32, MinimumLength = 32)]
        public string Sign { get; set; } = string.Empty;

        /// <summary>
        /// [可选]扩展参数
        /// </summary>
        public Dictionary<string, object> Expand { get; set; } = [];

        /// <summary>
        /// 自动校验 + 自动验签（子类可重写）
        /// </summary>
        public virtual string Validate(bool useBaseValidation = true)
        {
            // 1. 先做数据注解校验（必填、范围、长度等）
            if (useBaseValidation)
            {
                string validateError = RequestValidator.Validate(this);
                if (!string.IsNullOrEmpty(validateError))
                    return validateError;
            }

            // 2. 自动验签（所有请求统一执行）
            string signError = ValidateSign();
            if (!string.IsNullOrEmpty(signError))
                return signError;

            // 3. 全部校验通过
            return string.Empty;
        }

        /// <summary>
        /// 自动验签（可重写、可自定义）
        /// 规则：所有公共参数按固定顺序拼接 + 密钥 → MD5 32位 大写
        /// </summary>
        protected virtual string ValidateSign()
        {
            try
            {
                var appSecret = AppMemoryCache.GetAppSecret(AppId);
                var plainText = $"AppId={AppId}&Version={Version}&Timestamp={Timestamp}&Key={appSecret}";
                var mySign = EncryptMagic.HashMd5(plainText);

                if (!mySign.Equals(Sign, StringComparison.OrdinalIgnoreCase))
                {
                    return "签名验证失败，请检查参数或密钥";
                }

                return string.Empty;
            }
            catch
            {
                return "签名验证异常";
            }
        }
    }
}