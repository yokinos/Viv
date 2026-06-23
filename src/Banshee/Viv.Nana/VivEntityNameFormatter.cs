using MassTransit;

namespace Viv.Nana
{
    /// <summary>
    /// Exchange 命名规则：对于 NanaEnvelope&lt;T&gt;，取 T 的命名空间
    /// 例：NanaEnvelope&lt;TestApexEvent&gt; → Viv.EventContracts.Apex
    /// </summary>
    public class VivEntityNameFormatter : IEntityNameFormatter
    {
        public static readonly VivEntityNameFormatter Instance = new();

        public string FormatEntityName<T>()
        {
            var type = typeof(T);

            // NanaEnvelope<T> → 用 T 的命名空间
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NanaEnvelope<>))
            {
                var innerType = type.GetGenericArguments()[0];
                return innerType.Namespace ?? "Viv";
            }

            return type.Namespace ?? type.Name;
        }
    }
}
