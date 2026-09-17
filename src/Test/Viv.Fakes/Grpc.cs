using Grpc.Core;

namespace Viv.Fakes;

/// <summary>
/// 空请求流替身 —— <see cref="MoveNext"/> 立刻返回 false（流里一条消息都没有）。
///
/// 三个流替身都住在同一个文件里：它们只在「手工构造 <c>ServerCallContext</c>」时成组出现，
/// 拆开反而要在三个文件之间来回跳。
/// </summary>
public sealed class EmptyStreamReader<T> : IAsyncStreamReader<T>
{
    public T Current => default!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

/// <summary>空响应流替身（服务端写方向）—— 写进去就丢，只求不炸。</summary>
public sealed class DummyServerStreamWriter<T> : IServerStreamWriter<T>
{
    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
        => Task.CompletedTask;
}

/// <summary>空请求流替身（客户端写方向）—— 写进去就丢，只求不炸。</summary>
public sealed class DummyClientStreamWriter<T> : IClientStreamWriter<T>
{
    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
        => Task.CompletedTask;

    public Task CompleteAsync()
        => Task.CompletedTask;
}
