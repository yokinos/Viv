using System.Collections.Concurrent;
using Viv.Delusion.Magic;
using Viv.Nana;

namespace Viv.Outbox.Core
{
    /// <summary>事件类型名 → 闭合发送器。解析不出来返回 null，调用方负责置 Failed 并记 Error。</summary>
    internal interface IOutboxEnvelopeSenderFactory
    {
        IOutboxEnvelopeSender? Resolve(string eventTypeName);
    }

    /// <summary>
    /// 按 <c>EventType</c> 字符串解析出事件类型，并给出对应的闭合发送器。
    ///
    /// 类型索引与发送器都缓存在静态字段上，整个进程只建一次：索引要扫全部程序集，
    /// 每轮轮询重建是纯浪费；发送器不持有任何作用域内的东西（发布器逐次传入），也没理由按 scope 重建。
    /// </summary>
    internal sealed class OutboxEnvelopeSenderFactory : IOutboxEnvelopeSenderFactory
    {
        /// <summary>事件类型名 → 类型。同时按 FullName 与程序集限定名建索引。</summary>
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> EventTypes =
            new(BuildTypeIndex, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>事件类型名 → 发送器。null 也会被缓存 —— 解析不出的类型不会再白扫一遍索引。</summary>
        private static readonly ConcurrentDictionary<string, IOutboxEnvelopeSender?> Senders = new(StringComparer.Ordinal);

        public IOutboxEnvelopeSender? Resolve(string eventTypeName)
        {
            if (string.IsNullOrWhiteSpace(eventTypeName)) return null;
            return Senders.GetOrAdd(eventTypeName, Build);
        }

        /// <summary>仅供测试与排查：事件类型名能否解析成类型。</summary>
        internal static Type? ResolveEventType(string eventTypeName)
        {
            if (string.IsNullOrWhiteSpace(eventTypeName)) return null;

            var index = EventTypes.Value;
            if (index.TryGetValue(eventTypeName, out var type)) return type;

            // 索引里没有：兜底给 Type.GetType —— 程序集被强制加载后仍解析得到的（如闭合泛型事件），
            // 名字形态与索引键对不上，但 GetType 认得。
            var fallback = Type.GetType(eventTypeName, throwOnError: false);
            return fallback is not null && typeof(NanaEvent).IsAssignableFrom(fallback) ? fallback : null;
        }

        private static IOutboxEnvelopeSender? Build(string eventTypeName)
        {
            var eventType = ResolveEventType(eventTypeName);
            if (eventType is null || !typeof(NanaEvent).IsAssignableFrom(eventType)) return null;
            if (eventType.IsAbstract || eventType.IsGenericTypeDefinition) return null;

            var senderType = typeof(OutboxEnvelopeSender<>).MakeGenericType(eventType);
            return (IOutboxEnvelopeSender?)Activator.CreateInstance(senderType);
        }

        private static IReadOnlyDictionary<string, Type> BuildTypeIndex()
        {
            // 业务 Core 常是懒加载程序集，不先强制加载，扫出来的事件类型是残缺的 ——
            // 而那会表现成「库里的消息投递不出去了」，很难查。
            TypeScanMagic.ForceLoadReferencedAssemblies();

            var index = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var type in TypeScanMagic.ScanTypes<NanaEvent>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;

                if (type.FullName is { Length: > 0 } fullName)
                    index[fullName] = type;

                // 入队写的是 FullName；把 AQN 也收进来，兼容手工写入或将来改过的行
                if (type.AssemblyQualifiedName is { Length: > 0 } aqn)
                    index.TryAdd(aqn, type);
            }

            return index;
        }
    }
}
