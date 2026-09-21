using System.Globalization;
using System.Text;
using Viv.Momo.Enums;

namespace Viv.Momo
{
    /// <summary>
    /// 实体 → 物理表/列名的单一来源。EF、Dapper/<see cref="SqlMagic"/>、SchemaSynchronizer 都走这里，
    /// 同一实体在三条路径上必须落到同一张表、同一列。
    ///
    /// 按 provider 各一套、互不混用：
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// PostgreSQL：snake_case、不加引号。<c>AtUser</c>/<c>TenantId</c> → <c>at_user</c>/<c>tenant_id</c>。
    /// 算法对齐 EFCore.NamingConventions 的 <c>SnakeCaseNameRewriter</c>（<c>UseSnakeCaseNamingConvention</c>），
    /// 所以 EF 建出来的表，Dapper 批量 SQL 和 SchemaSynchronizer 的 DDL 都能打中。
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// SQL Server：CLR 声明的 PascalCase，方括号引用。<c>AtUser</c>/<c>TenantId</c> → <c>[AtUser]</c>/<c>[TenantId]</c>。
    /// EF 在 SQL Server 上不再套 snake_case（以前两边各一套，同步器靠去下划线模糊匹配把差异藏掉）。
    /// </description>
    /// </item>
    /// </list>
    ///
    /// 显式 <c>[Table]</c>/<c>[Column]</c> 的名字已经是物理名：只加引号、不再改写。
    /// Dapper 参数名始终是 CLR 属性名（<c>@Id</c>、<c>@TenantId</c>），与列的物理名无关。
    /// </summary>
    public static class MomoIdentifier
    {
        /// <summary>CLR 类名/属性名 → 该 provider 下的物理名（不含引号）。</summary>
        public static string ToPhysical(string clrName, DatabaseSourceType source)
        {
            if (string.IsNullOrEmpty(clrName)) return clrName;
            return source == DatabaseSourceType.PostgreSQL ? ToSnakeCase(clrName) : clrName;
        }

        /// <summary>已经是物理名：SQL Server 加方括号，PostgreSQL 原样（调用方保证是 snake_case）。</summary>
        public static string Quote(string physicalName, DatabaseSourceType source)
        {
            return source switch
            {
                DatabaseSourceType.SqlServer => $"[{physicalName}]",
                DatabaseSourceType.PostgreSQL => physicalName,
                _ => physicalName
            };
        }

        /// <summary>CLR 标识符一步到位：改写 + 加引号。SqlMagic / 表达式树走这条。</summary>
        public static string QuoteClr(string clrName, DatabaseSourceType source)
            => Quote(ToPhysical(clrName, source), source);

        /// <summary>
        /// 与 EFCore.NamingConventions 10.x <c>SnakeCaseNameRewriter</c> 同一套规则（InvariantCulture）。
        /// 钉住它是为了 EF 建表名和 Dapper 拼出来的标识符字节级一致。
        /// </summary>
        public static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var builder = new StringBuilder(name.Length + Math.Min(2, name.Length / 5));
            UnicodeCategory? previousCategory = null;

            for (var currentIndex = 0; currentIndex < name.Length; currentIndex++)
            {
                var currentChar = name[currentIndex];
                if (currentChar == '_')
                {
                    builder.Append('_');
                    previousCategory = null;
                    continue;
                }

                var currentCategory = char.GetUnicodeCategory(currentChar);
                switch (currentCategory)
                {
                    case UnicodeCategory.UppercaseLetter:
                    case UnicodeCategory.TitlecaseLetter:
                        if (previousCategory == UnicodeCategory.SpaceSeparator ||
                            previousCategory == UnicodeCategory.LowercaseLetter ||
                            previousCategory != UnicodeCategory.DecimalDigitNumber &&
                            previousCategory != null &&
                            currentIndex > 0 &&
                            currentIndex + 1 < name.Length &&
                            char.IsLower(name[currentIndex + 1]))
                        {
                            builder.Append('_');
                        }

                        currentChar = char.ToLower(currentChar, CultureInfo.InvariantCulture);
                        break;

                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.DecimalDigitNumber:
                        if (previousCategory == UnicodeCategory.SpaceSeparator)
                        {
                            builder.Append('_');
                        }
                        break;

                    default:
                        if (previousCategory != null)
                        {
                            previousCategory = UnicodeCategory.SpaceSeparator;
                        }
                        continue;
                }

                builder.Append(currentChar);
                previousCategory = currentCategory;
            }

            return builder.ToString();
        }
    }
}
