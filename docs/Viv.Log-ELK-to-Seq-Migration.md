# Viv.Log：ELK → Seq 迁移方案

## 概述

将 Viv.Log 的日志存储从 **ELK（Elasticsearch + Kibana）** 迁移到 **Seq**。

Seq 相比 ELK 的优势：
- 单一进程，无复杂集群管理
- Windows 原生支持，也支持 Docker/Linux
- 自带 UI，无需额外安装 Kibana
- 结构化日志查询语法（类 SQL），比 ES Query DSL 更直观
- 资源占用更低（特别是内存）

---

## 一、修改清单

### 1. `src/Banshee/Viv.Log/Viv.Log.csproj` — NuGet 包替换

| 操作 | 包名 |
|---|---|
| **移除** | `Elastic.Serilog.Sinks` (9.0.0) |
| **新增** | `Serilog.Sinks.Seq` (最新稳定版) |

```xml
<!-- 删除这行 -->
<PackageReference Include="Elastic.Serilog.Sinks" Version="9.0.0" />

<!-- 新增这行 -->
<PackageReference Include="Serilog.Sinks.Seq" Version="9.0.0" />
```

---

### 2. `src/Banshee/Viv.Log/LogOptions.cs` — 配置模型

**当前代码（ELK）：**

```csharp
public class LogOptions
{
    public LogType LogType { get; set; } = LogType.Serilog;
    public bool IsUseELK { get; set; } = false;
    public string ELKUrl { get; set; } = "http://localhost:9200";
    public string ELKApiKey { get; set; } = string.Empty;
    public string ELKUserName { get; private set; } = "elastic";
    public string ELKPassword { get; set; } = "viv_elk_77";
}

public class LoggerRegister
{
    public static void Initialize(LogOptions options)
    {
        if (options.IsUseELK && options.ELKUrl.IsNullOrEmpty())
        {
            throw new Exception("ELK地址不能为空");
        }
        VivConfigRegistry.Add(options);
    }
}
```

**修改为（Seq）：**

```csharp
public class LogOptions
{
    /// <summary>
    /// 日志框架类型  
    /// </summary>
    public LogType LogType { get; set; } = LogType.Serilog;

    /// <summary>
    /// 是否使用Seq
    /// </summary>
    public bool IsUseSeq { get; set; } = false;

    /// <summary>
    /// Seq服务地址
    /// </summary>
    public string SeqUrl { get; set; } = "http://localhost:5341";

    /// <summary>
    /// Seq API Key（可选，未配置则不传）
    /// </summary>
    public string SeqApiKey { get; set; } = string.Empty;
}

public class LoggerRegister
{
    public static void Initialize(LogOptions options)
    {
        if (options.IsUseSeq && options.SeqUrl.IsNullOrEmpty())
        {
            throw new Exception("Seq地址不能为空");
        }

        VivConfigRegistry.Add(options);
    }
}
```

**变更点：**
- `IsUseELK` → `IsUseSeq`
- `ELKUrl` → `SeqUrl`（默认端口从 9200 → 5341）
- `ELKApiKey` → `SeqApiKey`
- 删除 `ELKUserName`、`ELKPassword`（Seq 用 API Key 鉴权，无需用户名密码）

---

### 3. `src/Banshee/Viv.Log/SerilogDistributedLogger.cs` — Sink 配置

**当前代码（ELK Elasticsearch Sink）：**

```csharp
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Serilog;
using System;
using Viv.Vva;

namespace Viv.Log
{
    public class SerilogDistributedLogger : IDistributedLogger
    {
        private readonly ILogger _logger;

        public SerilogDistributedLogger()
        {
            var options = VivConfigRegistry.Get<LogOptions>() ?? new LogOptions();

            var factory = new LoggerConfiguration()
                 .MinimumLevel.Debug()
                 .Enrich.FromLogContext()
                 .WriteTo.Console()
                 .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

            // 启用 ELK
            if (options.IsUseELK)
            {
                var elasticUris = new[] { new Uri(options.ELKUrl ?? "http://localhost:9200") };
                factory.WriteTo.Elasticsearch(elasticUris, opts =>
                {
                    opts.DataStream = new DataStreamName("logs", "viv-distributed", "production");
                    opts.BootstrapMethod = BootstrapMethod.Failure;
                },
                transport =>
                {
                    if (!string.IsNullOrEmpty(options.ELKUserName) && !string.IsNullOrEmpty(options.ELKPassword))
                    {
                        transport.Authentication(new BasicAuthentication(options.ELKUserName, options.ELKPassword));
                    }
                    else if (!string.IsNullOrEmpty(options.ELKApiKey))
                    {
                        transport.Authentication(new ApiKey(options.ELKApiKey));
                    }
                });
            }

            _logger = factory.CreateLogger();
        }

        // ... Debug/Info/Warning/Error/Fatal 方法不变
    }
}
```

**修改为（Seq Sink）：**

```csharp
using Serilog;
using System;
using Viv.Vva;

namespace Viv.Log
{
    /// <summary>
    /// Serilog 分布式日志（纯代码实现，无注入，无ILogger）
    /// </summary>
    public class SerilogDistributedLogger : IDistributedLogger
    {
        private readonly ILogger _logger;

        public SerilogDistributedLogger()
        {
            var options = VivConfigRegistry.Get<LogOptions>() ?? new LogOptions();

            var factory = new LoggerConfiguration()
                 .MinimumLevel.Debug()
                 .Enrich.FromLogContext()
                 .WriteTo.Console()
                 .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);

            // 启用 Seq
            if (options.IsUseSeq)
            {
                factory.WriteTo.Seq(
                    serverUrl: options.SeqUrl ?? "http://localhost:5341",
                    apiKey: options.SeqApiKey.IsNullOrEmpty() ? null : options.SeqApiKey
                );
            }

            _logger = factory.CreateLogger();
        }

        public void Debug(string message, params object[] args) => _logger.Debug(message, args);
        public void Info(string message, params object[] args) => _logger.Information(message, args);
        public void Warning(string message, params object[] args) => _logger.Warning(message, args);
        public void Error(string message, params object[] args) => _logger.Error(message, args);
        public void Error(string message, Exception ex, params object[] args) => _logger.Error(ex, message, args);
        public void Fatal(string message, params object[] args) => _logger.Fatal(message, args);
        public void Fatal(string message, Exception ex, params object[] args) => _logger.Fatal(ex, message, args);
    }
}
```

**变更点：**
- 删除 3 个 Elasticsearch 相关 using（`Elastic.Ingest.Elasticsearch`、`Elastic.Ingest.Elasticsearch.DataStreams`、`Elastic.Serilog.Sinks`、`Elastic.Transport`）
- 删除 `using Viv.Vva;`（不再需要 `IsNullOrEmpty` 扩展 — 实际上还在用，保留）
- `options.IsUseELK` → `options.IsUseSeq`
- `factory.WriteTo.Elasticsearch(...)` 整个块 → `factory.WriteTo.Seq(serverUrl, apiKey)`
- Seq 的 `apiKey` 参数接受 `null`（表示无需认证），不需要复杂的 BasicAuth/ApiKey 分支

---

### 4. `src/Vivian/Viv.Aspire/Viv.Aspire.ServiceDefaults/AspireParameter.cs` — Aspire 参数

**当前：**

```csharp
public record DistributedLogConfig(bool IsEnabled, string ELKUrl, string ELKUsername, string ELKPassword);
```

**修改为：**

```csharp
public record DistributedLogConfig(bool IsEnabled, string SeqUrl, string SeqApiKey);
```

---

### 5. `src/Vivian/Viv.Aspire/Viv.Aspire.AppHost/viv.aspireparameter.json` — 默认配置

**当前：**

```json
"DistributedLogConfig": {
    "IsEnabled": false,
    "ELKUrl": "",
    "ELKUsername": "",
    "ELKPassword": ""
}
```

**修改为：**

```json
"DistributedLogConfig": {
    "IsEnabled": false,
    "SeqUrl": "http://localhost:5341",
    "SeqApiKey": ""
}
```

---

### 6. `docker/docker-compose.yml` — Docker 编排

**删除：** `elasticsearch` 和 `kibana` 两个 service（约 37 行）

**新增 Seq service：**

```yaml
  seq:
    image: datalust/seq:latest
    container_name: Viv_Seq
    environment:
      - ACCEPT_EULA=Y
    ports:
      - "5341:5341"   # Seq Web UI + API
      - "45341:45341"  # Seq Ingestion (可选，用于多端口)
    volumes:
      - seq_data:/data
    networks:
      - viv-net
    restart: unless-stopped
```

并在文件末尾的 `volumes:` 下添加：

```yaml
  seq_data:
```

---

### 7. `docker/docker.windows.txt` — Windows Docker 命令参考

**删除** 第 12-14 行的 ELK 相关命令，替换为：

```bash
# Seq
docker run -d --name Viv_Seq -p 5341:5341 -e ACCEPT_EULA=Y -v seq_data:/data datalust/seq:latest
```

> 注意：`ACCEPT_EULA=Y` 在 Windows CMD 中可能需要用 `$env:ACCEPT_EULA="Y"` 或直接在 PowerShell 中设置。

---

## 二、不影响的部分

以下文件/组件 **无需修改**：

| 文件 | 原因 |
|---|---|
| `IDistributedLogger.cs` | 接口定义与存储后端无关 |
| `VivWriteLogger.cs` | 静态门面，不感知底层 sink |
| `NoneLogger.cs` | 兜底实现，仅写控制台 |
| `ExceptionAnalyzer.cs` | 异常解析工具，与存储无关 |
| `LogType.cs` | 枚举 `None=0, Serilog=1`，保持不变 |
| `VivOptions.cs` | 引用的是 `LogOptions` 类型，字段名不变 |
| `VivRegister.cs` | 只依赖 `LogOptions` 和 `LogType` 枚举，无 ELK 耦合 |
| 所有业务代码调用处 | 通过 `IDistributedLogger` 接口调用，完全透明 |

---

## 三、迁移步骤（推荐顺序）

1. **修改 `LogOptions.cs`** — 属性重命名，编译通过
2. **修改 `SerilogDistributedLogger.cs`** — sink 替换
3. **修改 `Viv.Log.csproj`** — NuGet 包替换
4. **修改 `AspireParameter.cs` + `viv.aspireparameter.json`** — 参数对齐
5. **修改 `docker-compose.yml` + `docker.windows.txt`** — 基础设施
6. **`dotnet build`** 验证编译通过
7. **启动 Seq** — `docker compose up -d seq` 或 `docker run ... datalust/seq`
8. **配置 Seq API Key**（可选）— 浏览器打开 `http://localhost:5341` → Settings → API Keys → 创建 Key，填入 `viv.config.json` 的 `SeqApiKey`

---

## 四、Seq 默认端口速查

| 端口 | 用途 |
|---|---|
| `5341` | Web UI + Ingestion API（默认） |
| `45341` | 仅 Ingestion（无 UI，生产推荐） |

Seq 启动后浏览器访问 `http://localhost:5341` 即可查看实时日志流。
