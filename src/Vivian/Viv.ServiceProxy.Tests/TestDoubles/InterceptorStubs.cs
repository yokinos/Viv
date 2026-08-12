using Grpc.Core;

namespace Viv.ServiceProxy.Tests.TestDoubles
{
    /// <summary>空请求流（客户端/服务端流式调用测试桩）。</summary>
    internal sealed class EmptyStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    /// <summary>空响应流（服务端写测试桩）。</summary>
    internal sealed class DummyServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
            => Task.CompletedTask;
    }

    /// <summary>空请求流（客户端写测试桩）。</summary>
    internal sealed class DummyClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
            => Task.CompletedTask;

        public Task CompleteAsync()
            => Task.CompletedTask;
    }
}
