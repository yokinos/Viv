using System.Diagnostics;
using Viv.Contracts;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Wolverine;

namespace Viv.Nana.Core
{
    public class NanaEventPublisher : IVivEventPublisher
    {
        /// <summary>
        /// 发布 span 的名字。与 db.query / redis.command 同形：名字是常量，是哪条事件看 event 标签，
        /// 不按调用次数分配字符串、也不制造高基数 span 名。
        /// </summary>
        private const string SpanName = "mq.publish";

        private readonly IVivContext _context;
        private readonly IMessageBus _bus;
        private readonly ILoggerContract _logger;

        public NanaEventPublisher(IVivContext context, IMessageBus bus, ILoggerContract logger)
        {
            _context = context;
            _bus = bus;
            _logger = logger;
        }

        public async ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
            };

            using var activity = StartPublishSpan<T>(null);

            try
            {
                var started = Stopwatch.GetTimestamp();
                await _bus.PublishAsync(message);
                NanaMetrics.RecordPublish(typeof(T).Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"Publish failed for {typeof(T).Name}", ex);
            }
        }

        public async ValueTask<bool> PublishEnvelopeAsync<T>(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (envelope?.Content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            // 原样重发：不调 SnapshotWithHolder（不重新盖 holderId），投递的就是调用方递进来的那个信封
            using var activity = StartPublishSpan<T>(null);

            try
            {
                var started = Stopwatch.GetTimestamp();
                await _bus.PublishAsync(envelope);
                NanaMetrics.RecordPublish(typeof(T).Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"Publish failed for {typeof(T).Name}", ex);
            }
        }

        public async ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaEnvelope<T>
            {
                Content = content,
                Context = SnapshotWithHolder(_context.GetRawSnapshot()?.Clone()),
                DelaySecond = delayTTL.TotalSeconds
            };

            using var activity = StartPublishSpan<T>(delayTTL);

            try
            {
                await _bus.ScheduleAsync(message, delayTTL);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"SchedulePublish failed for {typeof(T).Name}", ex);
            }
        }

        public async ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (envelope?.Content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;
            cancellationToken.ThrowIfCancellationRequested();

            using var activity = StartPublishSpan<T>(delayTTL);

            try
            {
                await _bus.ScheduleAsync(envelope, delayTTL);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"SchedulePublish failed for {typeof(T).Name}", ex);
            }
        }

        /// <summary>
        /// 发一条发布 span。Kind 用 Producer：瀑布图上这是「我们主动往外发」，
        /// 与 Client（发出去等对方回）分得开。
        ///
        /// 父上下文取当前 span，所以 HTTP 里发的消息挂在同一条 trace 下；
        /// 没有（后台线程）时自成一条根 span。
        ///
        /// 没有监听者时返回 null，省的是每次发布白建一个 ActivityTagsCollection。
        /// </summary>
        private static Activity? StartPublishSpan<T>(TimeSpan? delay) where T : NanaEvent
        {
            if (!VivTracing.Source.HasListeners())
                return null;

            var tags = new ActivityTagsCollection { ["event"] = typeof(T).Name };

            if (delay.HasValue)
                tags["delay.ms"] = delay.Value.TotalMilliseconds;

            return VivTracing.Source.StartActivity(
                SpanName, ActivityKind.Producer, Activity.Current?.Context ?? default, tags);
        }

        private static VivContextContent SnapshotWithHolder(VivContextContent? snap)
        {
            snap ??= new VivContextContent();
            if (string.IsNullOrWhiteSpace(snap.HolderId))
            {
                snap.HolderId = LockHolderContext.CurrentHolderId;
            }

            return snap;
        }

        private VivConnectionException WrapMqException(string message, Exception ex)
        {
            _logger.Error(message, ex);
            return new VivConnectionException(VivConnType.RabbitMQ, message, ex);
        }
    }
}
