using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Text;
using Viv.Contracts.Options;

namespace Viv.Engine
{
    /// <summary>
    /// JwtBearer 对称密钥验证注册，网关与下游服务共用。
    /// 密钥/发行方/受众来自 appsettings.json 的 VivOptions.TokenOption。
    /// </summary>
    public static class VivJwtBearerHelper
    {
        /// <summary>
        /// 这份配置能不能撑起 JwtBearer 注册（<c>TokenOption</c> 存在且 <c>SecretKey</c> 非空）。
        ///
        /// 单独暴露成纯函数，是为了让「注册时到底装没装鉴权」在别处也能算出同一个答案：
        /// 管道装配阶段（RunVivApi）必须知道要不要 <c>UseAuthentication</c>，但拿不到注册时的返回值。
        /// 两边调同一个判定，不留第二个真值来源。
        /// </summary>
        public static bool CanRegister(TokenOptions? tokenOptions)
            => tokenOptions != null && !string.IsNullOrWhiteSpace(tokenOptions.SecretKey);

        /// <summary>
        /// 从 TokenOption 注册 JwtBearer 对称密钥验证。
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="tokenOptions">TokenOption；为 null 或 SecretKey 为空时：
        /// throwIfMissing 为 true 则抛异常（网关必需），为 false 则跳过注册（下游无 TokenOption 时保持匿名）</param>
        /// <param name="configureJwt">微调 JwtBearerOptions（events、挑战头等）</param>
        /// <param name="throwIfMissing">TokenOption 缺失时是否抛异常</param>
        /// <returns>是否成功注册了 JwtBearer 鉴权（TokenOption 缺失且未要求抛异常时返回 false）</returns>
        public static bool ConfigureJwtBearer(
            IServiceCollection services,
            TokenOptions? tokenOptions,
            Action<JwtBearerOptions>? configureJwt = null,
            bool throwIfMissing = true)
        {
            if (!CanRegister(tokenOptions))
            {
                if (throwIfMissing)
                {
                    throw new InvalidOperationException("需要配置 appsettings.json 的 VivOptions.TokenOption 节点（SecretKey/Issuer/Audience），用于 JwtBearer 对称密钥验证。");
                }
                return false;
            }

            // CanRegister 判过就不可能是 null；这里收窄成局部非空变量，后面几处不必再各写一个 !
            var token = tokenOptions!;
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(token.SecretKey));
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = token.Issuer,
                        ValidateAudience = true,
                        ValidAudience = token.Audience,
                        ValidateLifetime = true,
                        IssuerSigningKey = signingKey,
                        ClockSkew = TimeSpan.Zero // 关闭时钟偏移容错，严格校验过期时间
                    };
                    configureJwt?.Invoke(options);
                });

            services.AddAuthorization();
            return true;
        }
    }
}
