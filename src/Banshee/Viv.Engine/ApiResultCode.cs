using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Engine
{
    /// <summary>
    /// 接口统一通用业务状态码
    /// 规则说明：
    /// 1. 2xx 正数区间：全部代表请求正常成功类状态
    /// 2. -2xx 区间：参数校验、通用基础业务拦截错误
    /// 3. -4xx 区间：登录、Token、签名、账号身份相关鉴权错误
    /// 4. -6xx 区间：功能接口、数据、渠道访问权限类错误
    /// 5. -7xx 区间：数据/资源不存在类统一错误
    /// 6. -5xx 区间：服务底层、中间件、第三方、事务系统异常
    /// 7. 业务细分场景错误统一使用 BusinessError = -200，自定义提示文案即可，不新增业务专属枚举值
    /// </summary>
    public enum ApiResultCode
    {
        #region 2xx 成功区间-请求正常处理完成

        /// <summary>
        /// 请求处理成功
        /// </summary>
        [Description("请求处理成功")]
        Success = 200,

        /// <summary>
        /// 任务已接收，后台异步执行，当前无即时返回结果
        /// </summary>
        [Description("任务已接收，异步处理中")]
        Accepted = 201,

        #endregion

        #region -2xx 参数&基础业务拦截区间

        /// <summary>
        /// 通用业务自定义失败，所有细分业务场景统一使用该编码，文案动态传入
        /// </summary>
        [Description("业务操作失败")]
        Error = -200,

        /// <summary>
        /// 接口必填参数缺失，未传递关键入参
        /// </summary>
        [Description("缺少必要请求参数")]
        ParamMissing = -201,

        /// <summary>
        /// 参数格式不符合约定，如日期、手机号、数字格式错误
        /// </summary>
        [Description("请求参数格式不合法")]
        ParamFormatError = -202,

        /// <summary>
        /// 参数数值超出业务允许区间，如负数、超过最大值等
        /// </summary>
        [Description("参数值超出允许范围")]
        ParamRangeInvalid = -203,

        /// <summary>
        /// 重复提交操作，防重复点击、重复下单场景使用
        /// </summary>
        [Description("重复操作，请勿重复提交")]
        DuplicateSubmit = -204,

        /// <summary>
        /// 唯一索引冲突，数据库已存在相同唯一数据
        /// </summary>
        [Description("数据已存在，无法重复新增")]
        DataExists = -205,

        /// <summary>
        /// 接口请求频率超限，触发限流拦截
        /// </summary>
        [Description("请求频次过高，请稍后重试")]
        RequestLimit = -206,

        /// <summary>
        /// 文件上传失败，包含格式不支持、大小超限、上传IO异常
        /// </summary>
        [Description("文件上传失败")]
        UploadError = -207,

        #endregion

        #region -4xx 身份鉴权 Token 登录相关区间

        /// <summary>
        /// 账号异地登录
        /// </summary>
        [Description("账号异地登录")]
        TokenEmpty = -400,

        /// <summary>
        /// 请求头未携带Token身份凭证
        /// </summary>
        [Description("身份凭证为空，请登录后操作")]
        TokenInvalid = -401,

        /// <summary>
        /// Token已过有效期，需重新登录获取新凭证
        /// </summary>
        [Description("登录身份已过期，请重新登录")]
        TokenExpired = -402,

        /// <summary>
        /// 身份凭证无效
        /// </summary>
        [Description("身份凭证无效")]
        TokenKickOut = -403,

        /// <summary>
        /// 请求的资源不存在
        /// </summary>
        [Description("请求的资源不存在")]
        NotFound = -404,

        #endregion

        #region -6xx 功能&数据&渠道权限区间

        /// <summary>
        /// 登录账号无当前接口访问操作权限
        /// </summary>
        [Description("无接口操作权限")]
        NoPermission = -601,

        /// <summary>
        /// 账号仅能操作自身数据，越权访问他人业务数据拦截
        /// </summary>
        [Description("无当前数据访问权限")]
        DataScopeDenied = -602,

        /// <summary>
        /// 接口仅对内开放，不允许外网/前端直接调用
        /// </summary>
        [Description("该接口未对外开放访问")]
        ApiNotOpen = -603,

        /// <summary>第三方渠道、商户应用未完成授权配置</summary>
        [Description("应用/渠道未完成授权")]
        ChannelUnauthorized = -604,

        #endregion

        #region -5xx 系统底层中间件异常区间

        /// <summary>
        /// 未捕获全局未知服务异常，兜底错误码
        /// </summary>
        [Description("服务器内部未知异常")]
        ServerError = -500,

        /// <summary>
        /// 数据库增删改查执行异常、连接失败、SQL报错
        /// </summary>
        [Description("数据库操作异常")]
        DatabaseError = -501,

        /// <summary>
        /// Redis缓存读写、连接、序列化异常
        /// </summary>
        [Description("缓存服务操作异常")]
        CacheError = -502,

        /// <summary>
        /// RabbitMQ等消息队列生产/消费、连接异常
        /// </summary>
        [Description("消息队列服务异常")]
        MqError = -503,

        /// <summary>
        /// 调用微信、OSS、短信等第三方外部接口报错
        /// </summary>
        [Description("调用外部第三方服务失败")]
        ThirdApiError = -504,

        /// <summary>
        /// Saga、分布式事务执行回滚失败
        /// </summary>
        [Description("分布式事务执行失败")]
        DistributedTransError = -505,

        /// <summary>
        /// 服务熔断、降级、流量保护拦截
        /// </summary>
        [Description("服务触发限流熔断")]
        ServiceFuse = -506

        #endregion
    }
}