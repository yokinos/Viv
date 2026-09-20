using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// <see cref="IVivLocalEventScope"/> 的默认实现，与 <see cref="IVivLocalEventBus"/> 同为 Scoped。
    /// </summary>
    internal sealed class LocalEventScope : IVivLocalEventScope
    {
        private readonly IVivLocalEventBus _bus;
        private readonly ILoggerContract _logger;

        public LocalEventScope(IVivLocalEventBus bus, ILoggerContract logger)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            return RunCoreAsync(async () =>
            {
                await work().ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        /// <inheritdoc />
        public Task<TResult> RunAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            return RunCoreAsync(work, cancellationToken);
        }

        private async Task<TResult> RunCoreAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TResult result;
            try
            {
                result = await work().ConfigureAwait(false);
            }
            catch
            {
                _bus.Discard();
                throw;
            }

            try
            {
                // 与 HTTP / 消费者触发点同一条约定：handler 必须跑完，不跟停机令牌走
                await _bus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _bus.Discard();
                _logger.Error("主业务已完成，本地事件分发失败。已丢弃剩余事件。", ex);
                throw;
            }

            return result;
        }
    }
}
