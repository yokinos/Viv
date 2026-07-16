using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Viv.Aoi;
using Viv.Delusion.Magic;
using Viv.Elysia.Interface;

namespace Viv.Elysia.Request
{
    /// <summary>
    /// API 请求基类（统一公共参数 + 自动验签 + 自动校验）
    /// </summary>
    [Serializable]
    public class ApiRequestBase : IApiRequest
    {
        /// <summary>
        /// [必传]客户端AppId
        /// </summary>
        [Required]
        [Display(Name = "客户端AppId")]
        public long AppId { get; set; }

        /// <summary>
        /// [必传]服务器内部版本号
        /// </summary>
        [Required]
        [Display(Name = "服务器内部版本号")]
        [Range(1000, 9999)]
        public int Version { get; set; }

        /// <summary>
        /// [必传]时间戳(秒数)
        /// </summary>
        [Required]
        [Display(Name = "时间戳")]
        [Range(1000000000, 2000000000)]
        public long Timestamp { get; set; }

        /// <summary>
        ///[必传]签名字段
        /// </summary>
        [Required]
        [Display(Name = "签名字段")]
        [StringLength(32, MinimumLength = 32)]
        public string Sign { get; set; } = string.Empty;

        /// <summary>
        /// 自动校验
        /// </summary>
        public virtual string Validate(bool isSkipSignValidate = true)
        {
            string validateError = RequestParameterValidator.Validate(this);
            if (!string.IsNullOrEmpty(validateError))
                return validateError;

            if (!isSkipSignValidate)
            {
                var requestCheck = VivLocator.GetAutofaService<IApiRequestCheck>();
    
            }

            return string.Empty;
        }
    }
}