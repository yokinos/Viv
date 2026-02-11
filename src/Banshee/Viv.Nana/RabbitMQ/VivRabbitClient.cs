using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Viv.Nana.Models;

namespace Viv.Nana.RabbitMq
{
    /// <summary>
    /// RabbitMQ客户端封装类
    /// 功能：单例模式管理RabbitMQ连接和通道，自动声明队列/交换机并绑定，安全释放资源
    /// 依赖：继承自RabbitMQFactory（负责创建和管理RabbitMQ Connection）
    /// </summary>
    public class VivRabbitClient : RabbitMQFactory, IDisposable
    {
        private static readonly Lazy<VivRabbitClient> _instance = new(() => new VivRabbitClient(), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly ConcurrentDictionary<string, IChannel> _channels = new();
        private bool _disposed = false;
        private VivRabbitClient() { }

        public static VivRabbitClient GetInstance() => _instance.Value;

        /// <summary>
        /// 关闭指定队列的RabbitMQ通道（如果存在），并从缓存中移除
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public async Task CloseAsync(string queueName)
        {
            var key = GetCacheKey(queueName);
            if (_channels.TryGetValue(key, out var channel))
            {
                await SafeCloseChannelAsync(channel);
                _channels.TryRemove(key, out _);
            }
        }

        private static string GetCacheKey(string queueName) => $"{queueName}_{Environment.CurrentManagedThreadId}";

        /// <summary>
        /// 获取指定队列的RabbitMQ通道（复用已有通道，无则创建）
        /// </summary>
        /// <param name="model">队列配置模型（包含队列、交换机、路由键等信息）</param>
        /// <param name="deadLetter">死信队列配置模型（可选，为空则不创建死信队列）</param>
        /// <param name="cancellationToken">取消令牌（用于取消异步操作）</param>
        /// <returns>可用的RabbitMQ通道（IChannel）</returns>
        public async Task<IChannel> GetChannelAsync(QueueModel model, QueueModel? deadLetter = null, CancellationToken cancellationToken = default)
        {
            var key = GetCacheKey(model.QueueName);
            if (_channels.TryGetValue(key, out var oldChannel) && oldChannel.IsOpen)
            {
                return oldChannel;
            }

            if (oldChannel != null)
            {
                await SafeCloseChannelAsync(oldChannel);
            }

            // 获取连接
            var connection = await GetConnectionAsync(cancellationToken);

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,// 开启发布者确认
                publisherConfirmationTrackingEnabled: true,// 开启发布者返回
                outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(256)
            );
            var newChannel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
            await BindChannelAsync(newChannel, model, cancellationToken);

            if (deadLetter is not null)
            {
                var flag = DeadLetterValidator.Validate(model, deadLetter);
                if (flag)
                {
                    // 创建死信队列通道并绑定,随即关闭通道（死信队列不需要长连接）
                    using var deadletterChannel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
                    await BindChannelAsync(deadletterChannel, deadLetter, cancellationToken);
                }
            }

            _channels[key] = newChannel;
            return newChannel;
        }

        /// <summary>
        /// 声明队列、交换机，并绑定队列到交换机
        /// 注：RabbitMQ的声明操作是幂等的（已存在则忽略，不存在则创建）
        /// </summary>
        /// <param name="channel">待绑定的RabbitMQ通道</param>
        /// <param name="model">队列配置模型</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async static Task BindChannelAsync(IChannel channel, QueueModel model, CancellationToken cancellationToken = default)
        {
            // 参数校验：空值直接抛出ArgumentNullException（.NET6+新语法）
            ArgumentNullException.ThrowIfNull(channel);
            ArgumentNullException.ThrowIfNull(model);

            // 1. 声明队列
            await channel.QueueDeclareAsync(
                queue: model.QueueName,                // 队列名称
                durable: model.QueueDeclare.IsDurable, // 是否持久化（重启后队列不丢失）
                exclusive: model.QueueDeclare.IsExclusive,   // 是否排他（仅当前Connection可用，关闭后自动删除）
                autoDelete: model.QueueDeclare.IsAutoDelete, // 是否自动删除（无消费者时删除）
                arguments: model.QueueDeclare.Arguments,     // 队列扩展参数（如死信队列配置）
                cancellationToken: cancellationToken);       // 取消令牌

            // 2. 声明交换机
            await channel.ExchangeDeclareAsync(
                exchange: model.Exchange,                 // 交换机名称
                type: model.ExchangeType,                 // 交换机类型（Direct/Fanout/Topic/Headers）
                durable: model.ExchangeDeclare.IsDurable, // 是否持久化
                autoDelete: model.ExchangeDeclare.IsAutoDelete, // 是否自动删除
                arguments: model.ExchangeDeclare.Arguments,     // 交换机扩展参数
                cancellationToken: cancellationToken);          // 取消令牌

            // 3. 绑定队列到交换机（指定路由键，完成消息路由规则）
            await channel.QueueBindAsync(
                queue: model.QueueName,       // 队列名称
                exchange: model.Exchange,     // 交换机名称
                routingKey: model.RoutingKey, // 路由键（匹配规则）
                arguments: model.QueueBind.Arguments,   // 绑定扩展参数
                cancellationToken: cancellationToken); // 取消令牌
        }

        /// <summary>
        /// 安全关闭并释放Channel资源（捕获异常，避免释放失败影响主流程）
        /// </summary>
        /// <param name="channel">待释放的通道</param>
        private async Task SafeCloseChannelAsync(IChannel channel)
        {
            if (channel == null) return;
            try
            {
                if (channel.IsOpen)
                {
                    await channel.CloseAsync();
                }
                channel.Dispose();
            }
            catch
            {
                // 忽略释放失败异常
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                foreach (var channel in _channels.Values)
                {
                    SafeCloseChannelAsync(channel).Wait();
                }
                _channels.Clear();
                lock (_connectionLock)
                {
                    if (_connection != null)
                    {
                        _connection.Dispose();
                        _connection = null;
                    }
                }
            }

            // 标记为已释放
            _disposed = true;
        }
    }
}