using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Echo.Grpc;
using Viv.Sandrone.Impl;
using Viv.ServiceProxy.Protos;
using Viv.ServiceProxy.Tests.TestDoubles;

namespace Viv.ServiceProxy.Tests.Grpc
{
    /// <summary>
    /// VivGrpcInterceptor（Echo）客户端拦截器单测——P0 回归护栏：
    /// 断言注入头名对齐框架契约 x-viv-*（小写写盘）且四个调用形态全部覆盖。
    /// </summary>
    public class VivGrpcInterceptorTests
    {
        [Fact]
        public void AsyncUnaryCall_注入xViv契约头()
        {
            var captured = new Metadata();

            interceptor.AsyncUnaryCall(
                new GetTenantRequest(),
                ContextOf(UnaryMethod()),
                (req, ctx) =>
                {
                    CopyInto(captured, ctx.Options.Headers);
                    return DummyUnaryCall();
                });

            AssertVivHeaders(captured);
        }

        [Fact]
        public void AsyncServerStreamingCall_注入xViv契约头()
        {
            var captured = new Metadata();

            interceptor.AsyncServerStreamingCall(
                new GetTenantRequest(),
                ContextOf(ServerStreamingMethod()),
                (req, ctx) =>
                {
                    CopyInto(captured, ctx.Options.Headers);
                    return new AsyncServerStreamingCall<UserInfo>(
                        new EmptyStreamReader<UserInfo>(),
                        Task.FromResult(new Metadata()),
                        () => Status.DefaultSuccess,
                        () => new Metadata(),
                        () => { });
                });

            AssertVivHeaders(captured);
        }

        [Fact]
        public void AsyncClientStreamingCall_注入xViv契约头()
        {
            var captured = new Metadata();

            interceptor.AsyncClientStreamingCall(
                ContextOf(ClientStreamingMethod()),
                ctx =>
                {
                    CopyInto(captured, ctx.Options.Headers);
                    return new AsyncClientStreamingCall<UserInfo, UploadTenantUsersResponse>(
                        new DummyClientStreamWriter<UserInfo>(),
                        Task.FromResult(new UploadTenantUsersResponse()),
                        Task.FromResult(new Metadata()),
                        () => Status.DefaultSuccess,
                        () => new Metadata(),
                        () => { });
                });

            AssertVivHeaders(captured);
        }

        [Fact]
        public void AsyncDuplexStreamingCall_注入xViv契约头()
        {
            var captured = new Metadata();

            interceptor.AsyncDuplexStreamingCall(
                ContextOf(DuplexStreamingMethod()),
                ctx =>
                {
                    CopyInto(captured, ctx.Options.Headers);
                    return new AsyncDuplexStreamingCall<ChatMessage, ChatMessage>(
                        new DummyClientStreamWriter<ChatMessage>(),
                        new EmptyStreamReader<ChatMessage>(),
                        Task.FromResult(new Metadata()),
                        () => Status.DefaultSuccess,
                        () => new Metadata(),
                        () => { });
                });

            AssertVivHeaders(captured);
        }

        [Fact]
        public void 已存在的头不覆盖()
        {
            var preHeaders = new Metadata();
            preHeaders.Add(VivHeaderContract.AppId.ToLowerInvariant(), "999");
            var captured = new Metadata();

            interceptor.AsyncUnaryCall(
                new GetTenantRequest(),
                ContextOf(UnaryMethod(), preHeaders),
                (req, ctx) =>
                {
                    CopyInto(captured, ctx.Options.Headers);
                    return DummyUnaryCall();
                });

            Assert.Equal("999", captured.Get(VivHeaderContract.AppId)!.Value);
        }

        private readonly VivGrpcInterceptor interceptor = CreateInterceptor();

        private static VivGrpcInterceptor CreateInterceptor()
        {
            var accessor = new VivContextAccessor();
            var vivContext = new VivContext(accessor);
            vivContext.SetSnapshot(new VivContextContent { AppId = 1001, SubjectId = 77, UserId = 5 });
            return new VivGrpcInterceptor(vivContext);
        }

        private static void AssertVivHeaders(Metadata headers)
        {
            Assert.Equal("1001", headers.Get(VivHeaderContract.AppId)!.Value);
            Assert.Equal("77", headers.Get(VivHeaderContract.SubjectId)!.Value);
            Assert.Equal("5", headers.Get(VivHeaderContract.UserId)!.Value);
            // gRPC metadata 键写盘小写（HTTP 契约 x-viv-appId 混大小写）
            Assert.Contains(headers, e => e.Key == VivHeaderContract.AppId.ToLowerInvariant());
            Assert.Contains(headers, e => e.Key == VivHeaderContract.SubjectId.ToLowerInvariant());
            Assert.Contains(headers, e => e.Key == VivHeaderContract.UserId.ToLowerInvariant());
        }

        private static void CopyInto(Metadata target, Metadata? source)
        {
            if (source is null)
            {
                return;
            }

            foreach (var entry in source)
            {
                target.Add(entry);
            }
        }

        private static AsyncUnaryCall<GetTenantResponse> DummyUnaryCall()
            => new(
                Task.FromResult(new GetTenantResponse()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });

        private static ClientInterceptorContext<TRequest, TResponse> ContextOf<TRequest, TResponse>(
            Method<TRequest, TResponse> method, Metadata? headers = null)
            where TRequest : class
            where TResponse : class
            => new(method, "localhost", new CallOptions(headers ?? new Metadata()));

        private static Method<GetTenantRequest, GetTenantResponse> UnaryMethod()
            => new(MethodType.Unary, "Viv.ServiceProxy.Protos.TenantGrpcService", "GetTenant",
                MarshallerOf(GetTenantRequest.Parser), MarshallerOf(GetTenantResponse.Parser));

        private static Method<GetTenantRequest, UserInfo> ServerStreamingMethod()
            => new(MethodType.ServerStreaming, "Viv.ServiceProxy.Protos.TenantGrpcService", "ListTenantUsers",
                MarshallerOf(GetTenantRequest.Parser), MarshallerOf(UserInfo.Parser));

        private static Method<UserInfo, UploadTenantUsersResponse> ClientStreamingMethod()
            => new(MethodType.ClientStreaming, "Viv.ServiceProxy.Protos.TenantGrpcService", "UploadTenantUsers",
                MarshallerOf(UserInfo.Parser), MarshallerOf(UploadTenantUsersResponse.Parser));

        private static Method<ChatMessage, ChatMessage> DuplexStreamingMethod()
            => new(MethodType.DuplexStreaming, "Viv.ServiceProxy.Protos.TenantGrpcService", "Chat",
                MarshallerOf(ChatMessage.Parser), MarshallerOf(ChatMessage.Parser));

        private static Marshaller<T> MarshallerOf<T>(MessageParser<T> parser)
            where T : IMessage<T>
            => Marshallers.Create(m => m.ToByteArray(), b => parser.ParseFrom(b));
    }
}
