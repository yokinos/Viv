using Microsoft.AspNetCore.Builder;
using Scalar.AspNetCore;

namespace Viv.Engine
{
    public static class WebApiExtensions
    {
        /// <summary>
        /// 启用 Scalar API 文档（仅在非生产环境）
        /// </summary>
        public static void VivUseScalar(this WebApplication app, string title)
        {
            app.MapScalarApiReference(options =>
            {
                options.Title = title;
                options.Theme = ScalarTheme.Purple;
                options.Authentication = new ScalarAuthenticationOptions
                {
                    PreferredSecuritySchemes = ["Bearer"]
                };
            }).AllowAnonymous();
        }
    }
}
