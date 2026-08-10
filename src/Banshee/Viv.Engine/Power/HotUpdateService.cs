using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.Power
{
    /// <summary>
    /// 热更新服务。支持文件监听和自定义加载器。
    /// 调用 UseFile / UseLoader 选择加载方式后会立即触发首次加载。
    /// </summary>
    public sealed class HotUpdateService<T> : IDisposable
    {
        private readonly Lock _gate = new();

        /// <summary>
        /// 文件变更防抖窗口：FileSystemWatcher 一次保存会连发 LastWrite/Size/CreationTime 等多个事件，
        /// 窗口期内的事件合并为一次真正重载，避免并发加载多次、ValueChanged 重复触发。
        /// </summary>
        private static readonly TimeSpan FileChangeDebounce = TimeSpan.FromMilliseconds(300);

        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private CancellationTokenSource? _loopCts;
        private Func<CancellationToken, Task<T?>>? _loader;
        private T? _currentValue;
        private bool _disposed;

        public HotUpdateService() { }

        public T? CurrentValue
        {
            get { lock (_gate) { return _currentValue; } }
        }

        public event Action<T?>? ValueChanged;
        public event Action<Exception>? ErrorOccurred;

        public void UseLoader(Func<CancellationToken, Task<T?>> loader, TimeSpan? interval = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(loader);

            StopInternal();
            _loader = loader;

            if (interval.HasValue && interval.Value > TimeSpan.Zero)
                StartLoop(interval.Value);

            _ = RefreshAsync(CancellationToken.None);
        }

        public void UseFile(string path, Func<string, T?> parser, Encoding? encoding = null)
        {
            ThrowIfDisposed();
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(parser);

            StopInternal();

            var fullPath = Path.GetFullPath(path);
            encoding ??= Encoding.UTF8;

            _loader = async ct =>
            {
                var text = await ReadFileWithRetryAsync(fullPath, encoding, 3, ct).ConfigureAwait(false);
                return parser(text);
            };

            StartFileWatcher(fullPath);
            _ = RefreshAsync(CancellationToken.None);
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var loader = _loader;
            if (loader is null)
                throw new InvalidOperationException("No loader configured.");

            try
            {
                var value = await loader(cancellationToken).ConfigureAwait(false);
                SetCurrentValue(value);
            }
            catch (Exception ex)
            {
                RaiseErrorOccurred(ex);
            }
        }

        public void Stop()
        {
            ThrowIfDisposed();
            StopInternal();
        }

        public void Dispose()
        {
            if (_disposed) return;
            StopInternal();
            _disposed = true;
        }

        private void StopInternal()
        {
            lock (_gate)
            {
                _watcher?.Dispose();
                _watcher = null;

                _debounceTimer?.Dispose();
                _debounceTimer = null;

                _loopCts?.Cancel();
                _loopCts?.Dispose();
                _loopCts = null;
            }
        }

        private void StartFileWatcher(string fullPath)
        {
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory)) return;

            lock (_gate)
            {
                _watcher?.Dispose();

                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;

                // 防抖 Timer：初始不触发，事件到来时重置窗口
                _debounceTimer = new Timer(_ => OnFileChangedDebounced(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        private void StartLoop(TimeSpan interval)
        {
            lock (_gate)
            {
                _loopCts?.Cancel();
                _loopCts?.Dispose();
                _loopCts = new CancellationTokenSource();
            }

            _ = LoopAsync(interval, _loopCts.Token);
        }

        private async Task LoopAsync(TimeSpan interval, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RefreshAsync(ct).ConfigureAwait(false);
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_gate)
            {
                // 重置防抖窗口：一次保存连发的事件都被合并，仅窗口结束后触发一次真正重载
                _debounceTimer?.Change(FileChangeDebounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnFileChangedDebounced()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var loader = _loader;
                    if (loader is null) return;

                    var value = await loader(CancellationToken.None).ConfigureAwait(false);
                    SetCurrentValue(value);
                }
                catch (Exception ex)
                {
                    RaiseErrorOccurred(ex);
                }
            });
        }

        private void SetCurrentValue(T? newValue)
        {
            bool changed;

            lock (_gate)
            {
                changed = !EqualityComparer<T?>.Default.Equals(_currentValue, newValue);
                if (changed) _currentValue = newValue;
            }

            if (changed)
                RaiseValueChanged(newValue);
        }

        private void RaiseValueChanged(T? value)
        {
            var handler = ValueChanged;
            if (handler is null) return;

            try
            {
                handler.Invoke(value);
            }
            catch (Exception ex)
            {
                RaiseErrorOccurred(ex);
            }
        }

        private void RaiseErrorOccurred(Exception ex)
        {
            var handler = ErrorOccurred;
            if (handler is null) return;

            try
            {
                handler.Invoke(ex);
            }
            catch
            {
                // 事件处理器异常不应反向打爆热更新流程
            }
        }

        private static async Task<string> ReadFileWithRetryAsync(string fullPath, Encoding encoding, int maxRetries, CancellationToken ct)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await File.ReadAllTextAsync(fullPath, encoding, ct).ConfigureAwait(false);
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }

            return await File.ReadAllTextAsync(fullPath, encoding, ct).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
