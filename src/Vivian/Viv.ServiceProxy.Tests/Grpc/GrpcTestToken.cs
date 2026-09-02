using Viv.Contracts.Options;
using Viv.Delusion;

namespace Viv.ServiceProxy.Tests.Grpc
{
    internal static class GrpcTestToken
    {
        public const string Secret = "test-grpc-internal-token";
        public const string ServiceName = "viv.test.grpc";

        public static void EnsureRegistered()
        {
            VivConfigRegistry.Add(new VivInternalTokenOptions
            {
                InternalToken = Secret,
                ServiceName = ServiceName
            });
        }
    }
}
