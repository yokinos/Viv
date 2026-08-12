using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Viv.Delusion.Magic;

namespace Viv.Echo.Grpc
{
    /// <summary>
    /// gRPC 服务自动发现：扫描已加载程序集中「基类链上带 [BindServiceMethod] 的抽象基类」的具体实现类。
    /// grpc_csharp_plugin 生成的基类（如 TenantGrpcServiceBase）标 <c>[grpc::BindServiceMethod]</c> 但不继承
    /// <see cref="ServiceBase"/>，故用该特性沿基类链判定。宿主零手工接线：AddVivApi/RunVivApi 配置驱动时自动注册 + 映射。
    /// </summary>
    public static class VivGrpcDiscovery
    {
        /// <summary>
        /// 发现所有 gRPC 服务实现类（先强制加载引用程序集，再扫描）。
        /// </summary>
        public static IReadOnlyList<Type> FindServices()
        {
            TypeScanMagic.ForceLoadReferencedAssemblies();
            return TypeScanMagic.ScanTypes<object>(HasBindServiceMethodBase);
        }

        /// <summary>
        /// 基类链上是否存在带 <see cref="BindServiceMethodAttribute"/> 的抽象基类（排除 object）。
        /// </summary>
        private static bool HasBindServiceMethodBase(Type type)
        {
            for (var current = type.BaseType; current is not null && current != typeof(object); current = current.BaseType)
            {
                if (current.GetCustomAttribute<BindServiceMethodAttribute>(inherit: false) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将发现的 gRPC 服务注册进 DI（scoped，MapGrpcService 每次调用解析）。
        /// </summary>
        public static void RegisterServices(IServiceCollection services)
        {
            foreach (var type in FindServices())
            {
                services.AddScoped(type);
            }
        }

        /// <summary>
        /// 反射调用 <c>MapGrpcService&lt;T&gt;</c> 逐个映射发现的 gRPC 服务。
        /// </summary>
        public static void MapServices(IEndpointRouteBuilder endpoints)
        {
            var mapMethod = typeof(GrpcEndpointRouteBuilderExtensions)
                .GetMethod(nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService),
                           new[] { typeof(IEndpointRouteBuilder) });
            if (mapMethod is null)
            {
                return;
            }

            foreach (var type in FindServices())
            {
                mapMethod.MakeGenericMethod(type).Invoke(null, new object[] { endpoints });
            }
        }
    }
}
