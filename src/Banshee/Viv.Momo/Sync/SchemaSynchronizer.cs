using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viv.Contracts.Enums;
using Viv.Delusion.Magic;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Delusion;

namespace Viv.Momo.Sync
{
    /// <summary>
    /// 自动同步数据库表结构的工具。
    /// 原理：用 TypeScanMagic 扫描实体类得到预期 Schema → 查询 INFORMATION_SCHEMA 得到实际 Schema → Diff → 生成 DDL。
    /// 支持 PostgreSQL 和 SQL Server，由 DatabaseOptions.DatabaseSouce 决定。
    ///
    /// 使用方式：
    /// <code>
    ///   var sync = new SchemaSynchronizer(dbOptions);
    ///   var types = sync.ScanEntityTypes();                        // 1. 扫描实体
    ///   var expected = sync.BuildExpectedSchema(types);            // 2. 构建预期
    ///   var actual = await sync.FetchActualSchemaAsync();          // 3. 查询实际
    ///   var diff = sync.Diff(expected, actual);                   // 4. 对比
    ///   Console.WriteLine(SchemaSynchronizer.GenerateReport(diff));// 5. 报告
    ///   var ddl = sync.GenerateDdl(diff);                          // 6. 生成 DDL
    ///   // 7. 执行 DDL（用 IVivDbContext.ExecuteSqlListAsync）
    /// </code>
    /// </summary>
    public class SchemaSynchronizer
    {
        private readonly DatabaseOptions _dbOptions;
        private readonly List<FilterTypeOptions> _entityFilters;
        private readonly DatabaseSourceType _dbType;

        /// <summary>非主键列是否强制 NULL。true 时只有主键是 NOT NULL，其余全 NULL</summary>
        private readonly bool _nonPkNullable;

        /// <param name="dbOptions">数据库配置（连接串、EntityTypeOptions 等）</param>
        /// <param name="nonPkNullable">
        ///   默认 true：除主键外全部列标记为 NULL。适合 "实体即真相" 场景，不以 CLR 类型为准。
        ///   设 false 则按 CLR 类型判断（Nullable&lt;T&gt;、string 无 [Required] 等）。
        /// </param>
        public SchemaSynchronizer(DatabaseOptions dbOptions, bool nonPkNullable = true)
        {
            _dbOptions = dbOptions;
            _dbType = dbOptions.DatabaseSource;
            _entityFilters = dbOptions.EntityTypeOptions ?? [];
            _nonPkNullable = nonPkNullable;
        }

        // ==================== Public API ====================

        /// <summary>
        /// 用 TypeScanMagic 扫描 viv.config.json 中 EntityTypeOptions 配置的命名空间，
        /// 返回所有实现 IEntity 的实体类。这是"预期 Schema"的输入。
        /// </summary>
        public List<Type> ScanEntityTypes()
        {
            if (_entityFilters.Count == 0)
                return [];

            return TypeScanMagic.ScanRange(_entityFilters)
                .Distinct()
                .Where(t => typeof(IEntity).IsAssignableFrom(t))
                .ToList();
        }

        /// <summary>
        /// 反射实体类，读取 [Table]、[Column]、[Key]、[StringLength]、[NotMapped] 等特性，
        /// 构建"预期"的表和列定义。自动跳过导航属性（IEntity 类型或 ICollection&lt;T&gt; 等集合）。
        /// </summary>
        public List<TableInfo> BuildExpectedSchema(List<Type> entityTypes)
        {
            var tables = new List<TableInfo>();

            foreach (var type in entityTypes)
            {
                // [Table("xxx")] → 表名，无特性则用类名
                var tableAttr = type.GetCustomAttribute<TableAttribute>();
                var tableName = tableAttr?.Name ?? type.Name;

                var tableInfo = new TableInfo { Name = tableName };

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // 跳过 [NotMapped] 和导航属性（避免把关联对象当成列）
                    if (IsNotMapped(prop) || IsNavigationProperty(prop))
                        continue;

                    var columnInfo = BuildColumnInfo(prop);
                    tableInfo.Columns.Add(columnInfo);
                }

                tables.Add(tableInfo);
            }

            return tables;
        }

        /// <summary>
        /// 直连数据库，查询 INFORMATION_SCHEMA.TABLES + COLUMNS 获取当前实际 Schema。
        /// 根据 DatabaseOptions.DatabaseSouce 自动选 PostgreSQL 或 SQL Server 的查询方言。
        /// </summary>
        public async Task<List<TableInfo>> FetchActualSchemaAsync(CancellationToken cancellationToken = default)
        {
            var tables = new List<TableInfo>();

            var connStr = _dbOptions.MasterConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
                return tables;

            if (_dbType == DatabaseSourceType.PostgreSQL)
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);

                var tableNames = await QueryTableNamesPgAsync(conn, cancellationToken);
                foreach (var tableName in tableNames)
                {
                    var columns = await QueryColumnsPgAsync(conn, tableName, cancellationToken);
                    tables.Add(new TableInfo { Name = tableName, Columns = columns });
                }
            }
            else if (_dbType == DatabaseSourceType.SqlServer)
            {
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);

                var tableNames = await QueryTableNamesSqlServerAsync(conn, cancellationToken);
                foreach (var tableName in tableNames)
                {
                    var columns = await QueryColumnsSqlServerAsync(conn, tableName, cancellationToken);
                    tables.Add(new TableInfo { Name = tableName, Columns = columns });
                }
            }

            return tables;
        }

        /// <summary>
        /// 对比预期和实际 Schema，返回差异列表。
        /// 表名匹配忽略大小写和下划线（VivClientApp == vivclientapp）。
        /// 列级比较：类型字符串（如 nvarchar(100)）和 IsNullable 是否一致。
        /// </summary>
        public SyncDiffResult Diff(List<TableInfo> expected, List<TableInfo> actual)
        {
            var result = new SyncDiffResult();
            var actualMap = actual.ToDictionary(t => NormalizeTableName(t.Name), StringComparer.OrdinalIgnoreCase);
            var expectedMap = expected.ToDictionary(t => NormalizeTableName(t.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var expTable in expected)
            {
                if (actualMap.TryGetValue(NormalizeTableName(expTable.Name), out var actTable))
                {
                    // 表在两端都存在 → 比较列
                    var columnDiffs = DiffColumns(expTable.Name, expTable.Columns, actTable.Columns);
                    if (columnDiffs.Count > 0)
                    {
                        result.ModifiedTables.Add(new TableDiff
                        {
                            TableName = expTable.Name,
                            Type = DiffType.Modified,
                            ColumnDiffs = columnDiffs
                        });
                    }
                }
                else
                {
                    // 实体有但 DB 没有 → 新表
                    result.NewTables.Add(new TableDiff
                    {
                        TableName = expTable.Name,
                        Type = DiffType.Added,
                        ColumnDiffs = expTable.Columns.Select(c => new ColumnDiff
                        {
                            TableName = expTable.Name,
                            ColumnName = c.Name,
                            Type = DiffType.Added,
                            Expected = c
                        }).ToList()
                    });
                }
            }

            foreach (var actTable in actual)
            {
                // DB 有但实体没有 → 待删表
                if (!expectedMap.ContainsKey(NormalizeTableName(actTable.Name)))
                {
                    result.DeletedTables.Add(new TableDiff
                    {
                        TableName = actTable.Name,
                        Type = DiffType.Deleted,
                        ColumnDiffs = actTable.Columns.Select(c => new ColumnDiff
                        {
                            TableName = actTable.Name,
                            ColumnName = c.Name,
                            Type = DiffType.Deleted,
                            Actual = c
                        }).ToList()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 根据 Diff 结果生成 DDL 语句列表（CREATE TABLE、ALTER TABLE、DROP TABLE 等）。
        /// DDL 是纯字符串列表，需要自行执行（用 IVivDbContext.ExecuteSqlListAsync 或直接拿连接执行）。
        /// 执行顺序建议：先 DROP TABLE → CREATE TABLE → ALTER TABLE（先删依赖再建）。
        /// </summary>
        public List<string> GenerateDdl(SyncDiffResult diff)
        {
            var sqlList = new List<string>();

            // 先删表再建表，避免依赖冲突
            foreach (var table in diff.DeletedTables)
                sqlList.Add(GenerateDropTable(table.TableName));

            foreach (var table in diff.NewTables)
                sqlList.Add(GenerateCreateTable(table.TableName, table.ColumnDiffs.Select(d => d.Expected!).ToList()));

            foreach (var table in diff.ModifiedTables)
            {
                foreach (var colDiff in table.ColumnDiffs)
                {
                    switch (colDiff.Type)
                    {
                        case DiffType.Added:
                            sqlList.Add(GenerateAddColumn(table.TableName, colDiff.Expected!));
                            break;
                        case DiffType.Modified:
                            sqlList.Add(GenerateAlterColumn(table.TableName, colDiff.Expected!));
                            break;
                        case DiffType.Deleted:
                            sqlList.Add(GenerateDropColumn(table.TableName, colDiff.Actual!.Name));
                            break;
                    }
                }
            }

            return sqlList;
        }

        /// <summary>
        /// 生成可读的差异报告，方便人工审核变更内容后再执行。
        /// + 新增  - 删除  ~ 修改
        /// </summary>
        public static string GenerateReport(SyncDiffResult diff)
        {
            if (!diff.HasChanges)
                return "No schema changes detected.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Schema Diff Report ===");
            sb.AppendLine();

            if (diff.NewTables.Count > 0)
            {
                sb.AppendLine($"[+] New Tables ({diff.NewTables.Count}):");
                foreach (var t in diff.NewTables)
                    sb.AppendLine($"    + {t.TableName}");
                sb.AppendLine();
            }

            if (diff.DeletedTables.Count > 0)
            {
                sb.AppendLine($"[-] Deleted Tables ({diff.DeletedTables.Count}):");
                foreach (var t in diff.DeletedTables)
                    sb.AppendLine($"    - {t.TableName}");
                sb.AppendLine();
            }

            if (diff.ModifiedTables.Count > 0)
            {
                sb.AppendLine($"[~] Modified Tables ({diff.ModifiedTables.Count}):");
                foreach (var t in diff.ModifiedTables)
                {
                    sb.AppendLine($"    {t.TableName}:");
                    foreach (var c in t.ColumnDiffs)
                    {
                        var prefix = c.Type switch { DiffType.Added => "+", DiffType.Deleted => "-", _ => "~" };
                        sb.AppendLine($"      {prefix} {c.ColumnName} ({c.Expected?.FullSqlType ?? c.Actual?.FullSqlType})");
                    }
                }
            }

            return sb.ToString();
        }

        // ==================== Column info ====================

        /// <summary>
        /// 从 PropertyInfo 构建 ColumnInfo：[Table]→表名 [Column]→列名 [Key]→主键
        /// [StringLength]/[MaxLength]→类型长度 [DatabaseGenerated]→自增
        /// nullable 走 _nonPkNullable 控制逻辑
        /// </summary>
        private ColumnInfo BuildColumnInfo(PropertyInfo prop)
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? prop.Name;

            var clrType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var isPk = Attribute.IsDefined(prop, typeof(KeyAttribute));
            var isNullable = _nonPkNullable ? !isPk : IsClrNullable(prop);

            var maxLength = GetMaxLength(prop, clrType);
            var sqlType = MapToSqlType(prop, clrType, maxLength);
            var isAutoGen = Attribute.IsDefined(prop, typeof(DatabaseGeneratedAttribute));

            return new ColumnInfo
            {
                Name = colName,
                ClrType = clrType,
                SqlType = sqlType,
                MaxLength = maxLength,
                IsNullable = isNullable,
                IsPrimaryKey = isPk,
                IsAutoGenerated = isAutoGen
            };
        }

        // ==================== Column diff ====================

        private List<ColumnDiff> DiffColumns(string tableName, List<ColumnInfo> expected, List<ColumnInfo> actual)
        {
            var diffs = new List<ColumnDiff>();
            var expMap = expected.ToDictionary(c => NormalizeName(c.Name), StringComparer.OrdinalIgnoreCase);
            var actMap = actual.ToDictionary(c => NormalizeName(c.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var exp in expected)
            {
                if (actMap.TryGetValue(NormalizeName(exp.Name), out var act))
                {
                    if (!ColumnsMatch(exp, act))
                    {
                        diffs.Add(new ColumnDiff
                        {
                            TableName = tableName,
                            ColumnName = exp.Name,
                            Type = DiffType.Modified,
                            Expected = exp,
                            Actual = act
                        });
                    }
                }
                else
                {
                    diffs.Add(new ColumnDiff
                    {
                        TableName = tableName,
                        ColumnName = exp.Name,
                        Type = DiffType.Added,
                        Expected = exp
                    });
                }
            }

            foreach (var act in actual)
            {
                if (!expMap.ContainsKey(NormalizeName(act.Name)))
                {
                    diffs.Add(new ColumnDiff
                    {
                        TableName = tableName,
                        ColumnName = act.Name,
                        Type = DiffType.Deleted,
                        Actual = act
                    });
                }
            }

            return diffs;
        }

        /// <summary>比较两列的 SQL 类型和 Nullable 是否一致</summary>
        private bool ColumnsMatch(ColumnInfo expected, ColumnInfo actual)
        {
            return string.Equals(expected.FullSqlType, actual.SqlType, StringComparison.OrdinalIgnoreCase)
                && expected.IsNullable == actual.IsNullable;
        }

        // ==================== Type mapping ====================

        /// <summary>
        /// CLR 类型 → SQL 类型映射。
        /// string 有 [StringLength(n)] → varchar(n) / nvarchar(n)，
        /// 无长度限制 → text / nvarchar(max)。
        /// enum 按 int 处理。
        /// </summary>
        private string MapToSqlType(PropertyInfo prop, Type clrType, int? maxLength)
        {
            if (clrType.IsEnum) clrType = typeof(int);

            if (clrType == typeof(string))
            {
                if (maxLength.HasValue)
                    return _dbType == DatabaseSourceType.PostgreSQL ? $"varchar({maxLength})" : $"nvarchar({maxLength})";
                return _dbType == DatabaseSourceType.PostgreSQL ? "text" : "nvarchar(max)";
            }

            if (clrType == typeof(byte[]))
                return _dbType == DatabaseSourceType.PostgreSQL ? "bytea" : "varbinary(max)";

            // decimal 精度：优先读实体上的 [Precision(p,s)] / [Column(TypeName="decimal(p,s)")]，
            // 避免写死 (18,2) 把金额类等更高精度的列截断
            if (clrType == typeof(decimal))
            {
                var precisionAttr = prop.GetCustomAttribute<PrecisionAttribute>();
                if (precisionAttr != null)
                {
                    return _dbType == DatabaseSourceType.PostgreSQL
                        ? $"numeric({precisionAttr.Precision},{precisionAttr.Scale})"
                        : $"decimal({precisionAttr.Precision},{precisionAttr.Scale})";
                }

                var typeName = prop.GetCustomAttribute<ColumnAttribute>()?.TypeName;
                if (!string.IsNullOrWhiteSpace(typeName))
                    return typeName;
            }

            return _dbType switch
            {
                DatabaseSourceType.PostgreSQL => PgTypeMap.GetValueOrDefault(clrType, "text"),
                DatabaseSourceType.SqlServer => SqlServerTypeMap.GetValueOrDefault(clrType, "nvarchar(max)"),
                _ => "text"
            };
        }

        private static readonly Dictionary<Type, string> PgTypeMap = new()
        {
            [typeof(long)] = "bigint",
            [typeof(int)] = "integer",
            [typeof(short)] = "smallint",
            [typeof(bool)] = "boolean",
            [typeof(Guid)] = "uuid",
            [typeof(DateTime)] = "timestamp without time zone",
            [typeof(DateTimeOffset)] = "timestamp with time zone",
            [typeof(decimal)] = "numeric(18,2)",
            [typeof(double)] = "double precision",
            [typeof(float)] = "real",
            [typeof(TimeSpan)] = "interval",
        };

        private static readonly Dictionary<Type, string> SqlServerTypeMap = new()
        {
            [typeof(long)] = "bigint",
            [typeof(int)] = "int",
            [typeof(short)] = "smallint",
            [typeof(bool)] = "bit",
            [typeof(Guid)] = "uniqueidentifier",
            [typeof(DateTime)] = "datetime2",
            [typeof(DateTimeOffset)] = "datetimeoffset",
            [typeof(decimal)] = "decimal(18,2)",
            [typeof(double)] = "float",
            [typeof(float)] = "real",
            [typeof(TimeSpan)] = "time",
        };

        // ==================== DDL generation ====================

        private string GenerateCreateTable(string tableName, List<ColumnInfo> columns)
        {
            var qt = Quote(tableName);
            var colDefs = new List<string>();

            foreach (var col in columns)
            {
                var parts = new List<string> { Quote(col.Name), col.FullSqlType };
                if (!col.IsNullable) parts.Add("NOT NULL");
                if (col.IsPrimaryKey) parts.Add("PRIMARY KEY");
                if (col.IsAutoGenerated)
                {
                    // PG: GENERATED BY DEFAULT AS IDENTITY  SqlServer: IDENTITY(1,1)
                    parts.Add(_dbType == DatabaseSourceType.PostgreSQL
                        ? "GENERATED BY DEFAULT AS IDENTITY"
                        : "IDENTITY(1,1)");
                }
                if (col.DefaultValue != null)
                    parts.Add($"DEFAULT {col.DefaultValue}");
                colDefs.Add(string.Join(" ", parts));
            }

            if (columns.Any(c => c.IsPrimaryKey))
            {
                var pk = columns.First(c => c.IsPrimaryKey);
                colDefs.Add($"CONSTRAINT PK_{tableName} PRIMARY KEY ({Quote(pk.Name)})");
            }

            return $"CREATE TABLE {qt} (\n  {string.Join(",\n  ", colDefs)}\n);";
        }

        private string GenerateDropTable(string tableName)
            => $"DROP TABLE IF EXISTS {Quote(tableName)};";

        private string GenerateAddColumn(string tableName, ColumnInfo col)
        {
            var parts = new List<string> { Quote(col.Name), col.FullSqlType };
            if (!col.IsNullable) parts.Add("NOT NULL");
            if (col.DefaultValue != null) parts.Add($"DEFAULT {col.DefaultValue}");
            return $"ALTER TABLE {Quote(tableName)} ADD {string.Join(" ", parts)};";
        }

        /// <summary>
        /// 生成修改列的 DDL。
        /// PostgreSQL：ALTER COLUMN TYPE + ALTER COLUMN (SET|DROP) NOT NULL，需要两条子句。
        /// SqlServer：一条 ALTER COLUMN 搞定。
        /// </summary>
        private string GenerateAlterColumn(string tableName, ColumnInfo col)
        {
            if (_dbType == DatabaseSourceType.PostgreSQL)
            {
                var type = col.FullSqlType;
                var nullClause = col.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
                return $"ALTER TABLE {Quote(tableName)} ALTER COLUMN {Quote(col.Name)} TYPE {type}, ALTER COLUMN {Quote(col.Name)} {nullClause};";
            }
            else
            {
                var nullClause = col.IsNullable ? "NULL" : "NOT NULL";
                return $"ALTER TABLE {Quote(tableName)} ALTER COLUMN {Quote(col.Name)} {col.FullSqlType} {nullClause};";
            }
        }

        private string GenerateDropColumn(string tableName, string columnName)
            => $"ALTER TABLE {Quote(tableName)} DROP COLUMN IF EXISTS {Quote(columnName)};";

        // ==================== Quoting ====================

        /// <summary>
        /// 标识符引用：PG 用小写（vivclientapp），SqlServer 用方括号（[VivClientApp]）。
        /// 与 SqlMagic.QuoteIdentifier 逻辑一致。
        /// </summary>
        private string Quote(string name)
        {
            return _dbType switch
            {
                DatabaseSourceType.SqlServer => $"[{name}]",
                DatabaseSourceType.PostgreSQL => name.ToLowerInvariant(),
                _ => name
            };
        }

        // ==================== DB queries ====================

        private async Task<List<string>> QueryTableNamesPgAsync(NpgsqlConnection conn, CancellationToken ct)
        {
            var names = new List<string>();
            await using var cmd = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                names.Add(reader.GetString(0));
            return names;
        }

        private async Task<List<ColumnInfo>> QueryColumnsPgAsync(NpgsqlConnection conn, string tableName, CancellationToken ct)
        {
            var columns = new List<ColumnInfo>();
            await using var cmd = new NpgsqlCommand(
                "SELECT column_name, data_type, character_maximum_length, is_nullable, column_default FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t ORDER BY ordinal_position", conn);
            cmd.Parameters.AddWithValue("@t", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                columns.Add(new ColumnInfo
                {
                    Name = reader.GetString(0),
                    SqlType = NormalizePgType(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)),
                    MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    IsNullable = reader.GetString(3) == "YES",
                    DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
            return columns;
        }

        private async Task<List<string>> QueryTableNamesSqlServerAsync(SqlConnection conn, CancellationToken ct)
        {
            var names = new List<string>();
            await using var cmd = new SqlCommand(
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                names.Add(reader.GetString(0));
            return names;
        }

        private async Task<List<ColumnInfo>> QueryColumnsSqlServerAsync(SqlConnection conn, string tableName, CancellationToken ct)
        {
            var columns = new List<ColumnInfo>();
            await using var cmd = new SqlCommand(
                "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION", conn);
            cmd.Parameters.AddWithValue("@t", tableName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var dataType = reader.GetString(1);
                var maxLen = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2);
                columns.Add(new ColumnInfo
                {
                    Name = reader.GetString(0),
                    SqlType = NormalizeSqlServerType(dataType, maxLen),
                    MaxLength = maxLen,
                    IsNullable = reader.GetString(3) == "YES",
                    DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
            return columns;
        }

        // ==================== Type normalization ====================

        /// <summary>PG INFORMATION_SCHEMA 的 data_type → 与 MapToSqlType 一致的类型名，方便比较</summary>
        private string NormalizePgType(string pgType, int? maxLen)
        {
            var t = pgType.ToLowerInvariant();
            if (t == "character varying" && maxLen.HasValue) return $"varchar({maxLen})";
            if (t == "character varying") return "text";
            if (t == "timestamp with time zone") return "timestamp with time zone";
            if (t == "timestamp without time zone") return "timestamp without time zone";
            if (t == "double precision") return "double precision";
            if (t == "numeric") return "numeric(18,2)";
            return t;
        }

        /// <summary>SqlServer INFORMATION_SCHEMA 的 DATA_TYPE → 与 MapToSqlType 一致的类型名</summary>
        private string NormalizeSqlServerType(string sqlType, int? maxLen)
        {
            var t = sqlType.ToLowerInvariant();
            if (t == "nvarchar" && maxLen.HasValue) return $"nvarchar({maxLen})";
            if (t == "nvarchar") return "nvarchar(max)";
            if (t == "varchar" && maxLen.HasValue) return $"varchar({maxLen})";
            if (t == "varchar") return "varchar(max)";
            if (t == "varbinary") return "varbinary(max)";
            return t;
        }

        // ==================== Property filters ====================

        /// <summary>跳过 [NotMapped] 标记的属性</summary>
        private static bool IsNotMapped(PropertyInfo prop)
            => Attribute.IsDefined(prop, typeof(NotMappedAttribute));

        /// <summary>
        /// 跳过导航属性：类型实现 IEntity，或者是 ICollection/IEnumerable/List/HashSet&lt;T&gt; 且 T 实现 IEntity。
        /// 避免把关联表对象当成数据库列。
        /// </summary>
        private static bool IsNavigationProperty(PropertyInfo prop)
        {
            var type = prop.PropertyType;

            if (typeof(IEntity).IsAssignableFrom(type))
                return true;

            if (type.IsGenericType)
            {
                var genDef = type.GetGenericTypeDefinition();
                if (genDef == typeof(ICollection<>) || genDef == typeof(IEnumerable<>)
                    || genDef == typeof(List<>) || genDef == typeof(IList<>)
                    || genDef == typeof(HashSet<>) || genDef == typeof(ISet<>))
                {
                    var arg = type.GenericTypeArguments[0];
                    if (typeof(IEntity).IsAssignableFrom(arg))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取 [StringLength(n)] 或 [MaxLength(n)] 特性。
        /// 仅对 string 类型生效，其余类型返回 null。
        /// </summary>
        private static int? GetMaxLength(PropertyInfo prop, Type clrType)
        {
            if (clrType != typeof(string)) return null;

            var stringLength = prop.GetCustomAttribute<StringLengthAttribute>();
            if (stringLength != null) return stringLength.MaximumLength;

            var maxLength = prop.GetCustomAttribute<MaxLengthAttribute>();
            if (maxLength != null) return maxLength.Length;

            return null;
        }

        // ==================== Name normalization ====================

        /// <summary>
        /// 按 CLR 类型判断 nullable：Nullable&lt;T&gt; → true；string 无 [Required] → true；引用类型 → true；值类型 → false。
        /// 仅在 nonPkNullable=false 时生效。
        /// </summary>
        private static bool IsClrNullable(PropertyInfo prop)
        {
            if (prop.PropertyType.IsGenericType
                && prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                return true;

            if (prop.PropertyType == typeof(string))
                return !Attribute.IsDefined(prop, typeof(RequiredAttribute));

            return !prop.PropertyType.IsValueType;
        }

        /// <summary>去掉下划线并转小写，用于表名/列名的模糊匹配。Viv_Client_App == vivclientapp</summary>
        private static string NormalizeName(string name) => name.Replace("_", "").ToLowerInvariant();

        private static string NormalizeTableName(string name) => NormalizeName(name);
    }
}
