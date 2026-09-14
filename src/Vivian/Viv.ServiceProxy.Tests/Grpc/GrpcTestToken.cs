using Viv.Contracts.Options;

namespace Viv.ServiceProxy.Tests.Grpc
{
    internal static class GrpcTestToken
    {
        public const string Secret = "test-grpc-internal-token";
        public const string ServiceName = "viv.test.grpc";

        /// <summary>
        /// 内部密钥配置：客户端拦截器直接构造传入；服务端拦截器由容器注入，
        /// 需 <c>builder.Services.AddSingleton(GrpcTestToken.Options)</c> 注册。
        /// </summary>
        public static VivInternalTokenOptions Options { get; } = new()
        {
            InternalToken = Secret,
            ServiceName = ServiceName
        };
    }
}
