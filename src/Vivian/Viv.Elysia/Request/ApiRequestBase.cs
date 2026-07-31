using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Viv.Aoi;
using Viv.Delusion.Magic;
using Viv.Elysia.Interface;

namespace Viv.Elysia.Request
{
    /// <summary>
    /// API 请求基类
    /// </summary>
    [Serializable]
    public class ApiRequestBase : IApiRequest
    {
        [Required]
        [DisplayName("客户端AppId")]
        public long AppId { get; set; }

        [Required]
        [DisplayName("服务器内部版本号")]
        [Range(1000, 9999)]
        public int Version { get; set; }

        /// <summary>
        /// 自动校验
        /// </summary>
        public virtual string Validate(bool isSkipSignValidate = true)
        {
            string validateError = RequestParameterValidator.Validate(this);
            if (!string.IsNullOrEmpty(validateError))
            {
                return validateError;
            }

            return string.Empty;
        }
    }
}