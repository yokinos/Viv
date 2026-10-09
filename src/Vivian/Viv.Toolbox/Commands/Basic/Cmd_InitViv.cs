using Microsoft.Extensions.Configuration;
using Spectre.Console.Cli;
using Viv.Cli;
using Viv.Delusion.Magic;
using Viv.Entity.Enums;
using Viv.Momo;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Momo.Sync;
using Viv.Toolbox.CommandSetting.Basic;

namespace Viv.Toolbox.Commands.Basic
{
    /// <summary>
    /// 初始化 Viv 全部数据库：交互问数据库地址与初始操作员 → 建库 → 逐库同步表结构 → 播种超级管理员。
    /// 库清单硬编码在本命令里；没有实体的框架库只建库，表由框架自己建。
    /// 参数给全则全程不问（--yes），适合自动化。
    /// </summary>
    [VivCommand("initviv", "初始化Viv所有库（建库 + 表结构 + 初始操作员）")]
    public class Cmd_InitViv : AsyncCommand<InitVivSettings>
    {
        /// <summary>
        /// 库清单：库名 + 表名前缀 + 实体所在命名空间（前缀匹配；命名空间为空表示该库只有框架表）
        /// </summary>
        private static readonly (string Database, string TablePrefix, string EntityNamespace)[] Databases =
        [
            ("viv_apex_master", "At", "Viv.Entity.Database.Apex"),
            ("viv_herta_master", "Et", "Viv.Entity.Database.Herta"),
            ("viv_deepred_master", "Vt", "Viv.Entity.Database.DeepRed"),
            ("viv_ouroboros_core", "Ot", "Viv.Entity.Database.Ouroboros"),
            ("viv_saga_core", "", ""),
            ("viv_tickerq_core", "", "")
        ];

        private const string EntityAssembly = "Viv.Entity";

        private const string ApexDatabase = "viv_apex_master";

        /// <summary>
        /// 启用状态。以 EmStatus 枚举为准（Enabled = 0）——
        /// 注意用户角色类实体注释里写的"0禁用 1启用"是错的，别照注释写
        /// </summary>
        private static readonly byte StatusEnabled = (byte)EmStatus.Enabled;

        /// <summary>
        /// 初始客户端应用的对外 AppId
        /// </summary>
        private const string AdminClientAppId = "viv-rootadmin-app";

        private readonly IMomoDbContext _dbContext;

        private readonly IConfiguration _configuration;

        public Cmd_InitViv(IMomoDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        public async override Task<int> ExecuteAsync(CommandContext context, InitVivSettings settings, CancellationToken cancellationToken)
        {
            var template = _configuration["VivOptions:DatabaseOption:MasterConnectionString"];
            if (string.IsNullOrWhiteSpace(template))
            {
                Out.PrintlnError("未配置 VivOptions:DatabaseOption:MasterConnectionString，无法初始化");
                return 1;
            }

            if (!TryResolveConnection(template, settings, out var connection, out var reason))
            {
                Out.PrintlnError(reason);
                return 1;
            }

            if (!TryResolveOperator(settings, out var operatorName, out var nickName, out var phone, out var password, out var operatorReason))
            {
                Out.PrintlnError(operatorReason);
                return 1;
            }

            Out.Println(string.Empty);
            Out.Println($"目标：{Describe(connection)}");
            Out.Println($"库：{Databases.Length} 个　模式：{(settings.Drop ? "删表重建" : "增量补表")}");
            Out.Println($"初始操作员：{operatorName}（{nickName}）{phone}　超级管理员");

            if (!settings.Yes && !InputMagic.Confirm("确认执行？"))
            {
                Out.Println("已取消");
                return 0;
            }

            var createdDatabases = 0;
            var droppedTables = 0;
            var newTables = 0;
            var seeded = 0;
            var failed = 0;

            foreach (var (database, tablePrefix, entityNamespace) in Databases)
            {
                try
                {
                    if (!settings.SqlOnly)
                    {
                        var isNew = await CreateDatabaseIfMissingAsync(connection, database, cancellationToken);
                        if (isNew)
                        {
                            createdDatabases++;
                            Out.Println($"  [{database}] 已建库");
                        }
                    }

                    if (string.IsNullOrEmpty(entityNamespace))
                    {
                        Out.Println($"  [{database}] 仅建库（框架表由框架自行创建）");
                        continue;
                    }

                    var options = BuildOptions(connection, database, entityNamespace);
                    using var db = _dbContext.CreateContext(options);
                    if (db is null)
                    {
                        failed++;
                        Out.PrintlnError($"  [{database}] 无法创建上下文");
                        continue;
                    }

                    // 删表清单查 INFORMATION_SCHEMA 按域前缀过滤 —— 不依赖 EF 模型，也就不会碰到 VivInboxMessage 这类框架表
                    var dropSql = await BuildDropSqlAsync(db, tablePrefix, cancellationToken);
                    if (settings.Drop && dropSql.Count > 0)
                    {
                        Out.Println($"  [{database}] 待删 {dropSql.Count} 张表");
                        if (settings.SqlOnly)
                        {
                            dropSql.ForEach(Out.Println);
                        }
                        else
                        {
                            await db.ExecuteSqlListAsync(dropSql, isTxn: false, cancellationToken: cancellationToken);
                            droppedTables += dropSql.Count;
                        }
                    }

                    if (settings.SqlOnly)
                    {
                        continue;
                    }

                    var before = await CountTablesAsync(db, cancellationToken);
                    var created = await CreateTablesAsync(options, db, cancellationToken);
                    if (created > 0)
                    {
                        Out.Println($"  [{database}] 已建表 {created} 张（Id 列在首位）");
                    }

                    await db.SyncTableAsync(cancellationToken: cancellationToken);
                    var after = await CountTablesAsync(db, cancellationToken);
                    newTables += after - before;
                    Out.Println($"  [{database}] 表 {after} 张（新增 {after - before}）");

                    if (database == ApexDatabase)
                    {
                        seeded += await SeedClientAppAsync(db, cancellationToken);
                        seeded += await SeedAdminUserAsync(db, operatorName, nickName, phone, password, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Out.PrintlnError($"  [{database}] 失败：{ex.Message}");
                }
            }

            Out.Println(string.Empty);
            var summary = $"完成：建库 {createdDatabases}、删表 {droppedTables}、表新增 {newTables}、初始数据 {seeded} 行";
            if (failed > 0)
            {
                Out.PrintlnError($"{summary}、失败 {failed} 个库");
                return 1;
            }

            Out.PrintlnSuccess(summary);
            Out.Println($"登录账号：{operatorName}（超级管理员，自带完整权限）");
            return 0;
        }

        /// <summary>
        /// 解析数据库连接：命令行参数优先，缺的项交互询问；--yes 时不允许再问
        /// </summary>
        private static bool TryResolveConnection(string template, InitVivSettings settings,
            out string connection, out string reason)
        {
            connection = string.Empty;
            reason = string.Empty;

            var server = settings.Server;
            var account = settings.DbUser;
            var password = settings.DbPassword;

            if (settings.Yes && (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password)))
            {
                reason = "--yes 需要同时提供 --conn / --dbuser / --dbpwd";
                return false;
            }

            server ??= InputMagic.GetInput($"数据库地址 [{ExtractSegment(template, "Server") ?? "localhost,1433"}]", allowEmpty: true);
            if (string.IsNullOrWhiteSpace(server)) server = ExtractSegment(template, "Server") ?? "localhost,1433";

            account ??= InputMagic.GetInput($"数据库账号 [{ExtractSegment(template, "User Id") ?? "sa"}]", allowEmpty: true);
            if (string.IsNullOrWhiteSpace(account)) account = ExtractSegment(template, "User Id") ?? "sa";

            password ??= InputMagic.GetInput("数据库密码", secret: true);

            connection = ReplaceSegment(ReplaceSegment(ReplaceSegment(template, "Server", server), "User Id", account), "Password", password);
            return true;
        }

        /// <summary>
        /// 解析初始操作员：命令行参数优先，缺的项交互询问；手机号必填
        /// </summary>
        private static bool TryResolveOperator(InitVivSettings settings,
            out string userName, out string nickName, out string phone, out string password, out string reason)
        {
            userName = settings.UserName ?? string.Empty;
            nickName = settings.NickName ?? string.Empty;
            phone = settings.Phone ?? string.Empty;
            password = settings.Password ?? string.Empty;
            reason = string.Empty;

            if (settings.Yes)
            {
                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(phone))
                {
                    reason = "--yes 需要同时提供 --user / --pwd / --phone";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(nickName)) nickName = userName;
                return true;
            }

            if (string.IsNullOrWhiteSpace(userName)) userName = InputMagic.GetInput("操作员用户名");
            if (string.IsNullOrWhiteSpace(nickName)) nickName = InputMagic.GetInput($"操作员昵称 [{userName}]", allowEmpty: true);
            if (string.IsNullOrWhiteSpace(nickName)) nickName = userName;
            if (string.IsNullOrWhiteSpace(phone)) phone = InputMagic.GetInput("操作员手机号");

            if (string.IsNullOrWhiteSpace(password))
            {
                while (true)
                {
                    var first = InputMagic.GetInput("操作员密码", secret: true);
                    var second = InputMagic.GetInput("确认密码", secret: true);
                    if (first == second)
                    {
                        password = first;
                        break;
                    }

                    Out.PrintlnError("两次输入不一致，请重新输入");
                }
            }

            return true;
        }

        /// <summary>
        /// 播种客户端应用：后端很多数据（菜单、组织、版本）都挂在 AtClientApp.Id 下，
        /// 缺这条记录后台取不到任何东西，而它又不能为空，所以必须初始化
        /// </summary>
        private static async Task<int> SeedClientAppAsync(IMomoDbContext db, CancellationToken cancellationToken)
        {
            var exists = await db.FindScalarAsync<long?>(
                "SELECT TOP 1 Id FROM AtClientApp WHERE AppId = @appId AND IsDeleted = 0",
                new { appId = AdminClientAppId },
                cancellationToken);

            if (exists is not null and not 0)
            {
                Out.Println($"  [{ApexDatabase}] 客户端应用 {AdminClientAppId} 已存在，跳过");
                return 0;
            }

            var secret = Guid.NewGuid().ToString("N");

            await db.ExecuteSqlAsync(
                @"INSERT INTO AtClientApp (Id, AppId, Name, Platform, AppSecret, Source, Remark, Status, IsDeleted, CreatedAt)
                  VALUES (@id, @appId, @name, @platform, @secret, @source, @remark, @status, 0, @createdAt)",
                new
                {
                    id = IdMagic.NextId(),
                    appId = AdminClientAppId,
                    name = "Viv管理后台",
                    platform = (byte)EmAppPlatform.Web,
                    secret,
                    source = (byte)EmAppSouce.Viv,
                    remark = "系统初始化创建",
                    status = StatusEnabled,
                    createdAt = DateTime.Now
                },
                cancellationToken);

            Out.Println($"  [{ApexDatabase}] 客户端应用已创建：AppId={AdminClientAppId}　AppSecret={secret}");
            return 1;
        }

        /// <summary>
        /// 播种超级管理员账号。超管无视授权（IsSuperAdmin = true 直接拿最大数据权限），
        /// 所以不建角色、也不建用户-角色关联。
        /// 原生 SQL 读写 —— 每个库的上下文模型由宿主配置决定，按库切换时不可靠。
        /// </summary>
        private static async Task<int> SeedAdminUserAsync(IMomoDbContext db, string userName, string nickName,
            string phone, string password, CancellationToken cancellationToken)
        {
            var inserted = 0;
            var now = DateTime.Now;

            var userId = await db.FindScalarAsync<long?>(
                "SELECT TOP 1 Id FROM AtUser WHERE Name = @name AND IsDeleted = 0",
                new { name = userName },
                cancellationToken);

            if (userId is null or 0)
            {
                // 密码算法与登录一致：Md5(密码 + 盐)，见 Viv.Apex.Core/Impl/Login/LoginImplBase.cs:129
                var salt = Guid.NewGuid().ToString("N");
                userId = IdMagic.NextId();

                await db.ExecuteSqlAsync(
                    @"INSERT INTO AtUser (Id, UserType, Name, NickName, Phone, Password, Salt, IsSuperAdmin, Status, IsDeleted, CreatedAt)
                      VALUES (@id, @userType, @name, @nickName, @phone, @password, @salt, 1, @status, 0, @createdAt)",
                    new
                    {
                        id = userId,
                        userType = (int)EmUserType.Master,
                        name = userName,
                        nickName,
                        phone,
                        password = EncryptMagic.HashMd5($"{password}{salt}"),
                        salt,
                        status = StatusEnabled,
                        createdAt = now
                    },
                    cancellationToken);

                inserted++;
                Out.Println($"  [{ApexDatabase}] 初始用户 {userName} 已创建（超级管理员）");
            }
            else
            {
                Out.Println($"  [{ApexDatabase}] 初始用户 {userName} 已存在，跳过");
            }

            return inserted;
        }

        /// <summary>
        /// 建表：走框架自己的 DDL 生成器，只执行 CREATE TABLE，并让 [Id] 落在第一列。
        /// EF 的 EnsureCreatedAsync（SyncTableAsync 内部）按"派生类属性在前、基类在后"发现属性，
        /// 而 Id 声明在基类 EntityBase 里，建出来会掉到最后一列。
        /// 只取 CREATE TABLE —— GenerateDdl 里还有 DROP/ALTER，框架表（VivInboxMessage 等）
        /// 不在实体模型里会被判成多余表，本命令绝不能删它。
        /// </summary>
        private static async Task<int> CreateTablesAsync(DatabaseOptions options, IMomoDbContext db, CancellationToken cancellationToken)
        {
            var sync = new SchemaSynchronizer(options, allowAlterColumn: false);
            var entityTypes = sync.ScanEntityTypes();
            if (entityTypes.Count == 0)
            {
                return 0;
            }

            var diff = sync.Diff(sync.BuildExpectedSchema(entityTypes), await sync.FetchActualSchemaAsync(cancellationToken));
            if (!diff.HasChanges)
            {
                return 0;
            }

            var ddl = sync.GenerateDdl(diff)
                .Where(sql => sql.TrimStart().StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                .Select(MoveIdColumnFirst)
                .ToList();

            if (ddl.Count > 0)
            {
                await db.ExecuteSqlListAsync(ddl, isTxn: false, cancellationToken: cancellationToken);
            }

            return ddl.Count;
        }

        /// <summary>
        /// 把多行 CREATE TABLE 里以 [Id] 开头的那一列移到第一列位置，并重排逗号（最后一列不带）
        /// </summary>
        private static string MoveIdColumnFirst(string sql)
        {
            var lines = sql.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 3)
            {
                return sql;
            }

            var columns = lines[1..^1].ToList();
            var index = columns.FindIndex(line => line.TrimStart().StartsWith("[Id]", StringComparison.OrdinalIgnoreCase));
            if (index <= 0)
            {
                return sql;
            }

            var idColumn = columns[index].Trim().TrimEnd(',');
            columns.RemoveAt(index);
            columns.Insert(0, idColumn);

            var rebuilt = columns
                .Select((line, i) => "  " + line.Trim().TrimEnd(',') + (i == columns.Count - 1 ? string.Empty : ","))
                .ToList();

            return string.Join("\n", new[] { lines[0] }.Concat(rebuilt).Append(lines[^1]));
        }

        /// <summary>
        /// 建库（幂等）：查 sys.databases 判断存在，不存在才 CREATE DATABASE。
        /// CREATE DATABASE 不能进事务，所以 isTxn 传 false。
        /// </summary>
        private async Task<bool> CreateDatabaseIfMissingAsync(string connection, string database, CancellationToken cancellationToken)
        {
            var masterOptions = BuildOptions(connection, "master", null);
            using var master = _dbContext.CreateContext(masterOptions);
            if (master is null)
            {
                throw new InvalidOperationException("无法连接到 master 库");
            }

            var exists = await master.FindScalarAsync<int>(
                "SELECT COUNT(1) FROM sys.databases WHERE name = @name",
                new { name = database },
                cancellationToken);

            if (exists > 0)
            {
                return false;
            }

            await master.ExecuteSqlListAsync([$"CREATE DATABASE [{database}]"], isTxn: false, cancellationToken: cancellationToken);
            return true;
        }

        /// <summary>
        /// 生成删表 SQL：按域前缀查当前库的表，带 OBJECT_ID 判定，可重复执行。
        /// 前缀过滤天然排除了 VivInboxMessage / VivOutboxMessage 这类框架表。
        /// </summary>
        private static async Task<List<string>> BuildDropSqlAsync(IMomoDbContext db, string tablePrefix, CancellationToken cancellationToken)
        {
            var tables = await db.FindListAsync<string>(
                @"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
                  WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME LIKE @prefix + '%'
                  ORDER BY TABLE_NAME",
                new { prefix = tablePrefix },
                cancellationToken);

            return tables
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => $"IF OBJECT_ID('dbo.{name}', 'U') IS NOT NULL DROP TABLE [dbo].[{name}];")
                .ToList();
        }

        /// <summary>
        /// 按目标库拼配置：换掉连接串里的 Database，实体只扫指定命名空间（前缀匹配）
        /// </summary>
        private static DatabaseOptions BuildOptions(string connection, string database, string? entityNamespace)
        {
            var options = new DatabaseOptions
            {
                DatabaseSource = DatabaseSourceType.SqlServer,
                MasterConnectionString = ReplaceDatabase(connection, database)
            };

            if (!string.IsNullOrEmpty(entityNamespace))
            {
                options.EntityTypeOptions.Add(new FilterTypeOptions
                {
                    AssemblyName = EntityAssembly,
                    Namespace = entityNamespace
                });
            }

            return options;
        }

        /// <summary>
        /// 替换连接串里的 Database / Initial Catalog 段
        /// </summary>
        private static string ReplaceDatabase(string connection, string database)
            => System.Text.RegularExpressions.Regex.Replace(
                connection,
                @"(?i)(database|initial\s+catalog)\s*=\s*[^;]*",
                $"Database={database}");

        /// <summary>
        /// 替换连接串里的某个键（值不存在则追加）
        /// </summary>
        private static string ReplaceSegment(string connection, string key, string value)
        {
            var pattern = key.Replace(" ", @"\s+");
            var replaced = System.Text.RegularExpressions.Regex.Replace(
                connection,
                $@"(?i){pattern}\s*=\s*[^;]*",
                $"{key}={value}");

            return replaced == connection && !System.Text.RegularExpressions.Regex.IsMatch(connection, $@"(?i){pattern}\s*=")
                ? $"{replaced.TrimEnd(';')};{key}={value}"
                : replaced;
        }

        /// <summary>
        /// 从连接串取某个键的值（取不到返回 null）
        /// </summary>
        private static string? ExtractSegment(string connection, string key)
        {
            var pattern = key.Replace(" ", @"\s+");
            var match = System.Text.RegularExpressions.Regex.Match(connection, $@"(?i){pattern}\s*=\s*([^;]*)");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        /// <summary>
        /// 报表用：不显示账号密码，只留主机与库
        /// </summary>
        private static string Describe(string connection)
        {
            var parts = connection.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.StartsWith("server", StringComparison.OrdinalIgnoreCase)
                         || x.StartsWith("data source", StringComparison.OrdinalIgnoreCase));
            return string.Join(";", parts);
        }

        /// <summary>
        /// 当前库里的基础表数量（用于算新增）
        /// </summary>
        private static async Task<int> CountTablesAsync(IMomoDbContext db, CancellationToken cancellationToken)
            => await db.FindScalarAsync<int>(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
                null,
                cancellationToken);
    }
}
