<p align="center">
  <img src="https://img.shields.io/github/actions/workflow/status/yokinos/Viv/dotnet.yml?style=flat&logo=githubactions&logoColor=white&label=CI" />
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet" />
  <img src="https://img.shields.io/badge/C%23-13.0-239120?style=flat&logo=csharp" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=flat&logo=postgresql" />
  <img src="https://img.shields.io/badge/SQL_Server-2022-CC2927?style=flat&logo=microsoftsqlserver" />
  <img src="https://img.shields.io/badge/Redis-7.x-DC382D?style=flat&logo=redis" />
  <img src="https://img.shields.io/badge/RabbitMQ-3.x-FF6600?style=flat&logo=rabbitmq" />
  <img src="https://img.shields.io/badge/Aspire-13.x-8250DF?style=flat&logo=dotnet" />
</p>

# Viv

基于 **.NET 10** 的微服务基础设施框架。从数据访问、消息队列、缓存、认证到网关编排，提供一站式基础设施能力。业务侧只需声明 **实体 + Service + Repository**，框架自动完成扫描注册、租户隔离、统一响应与运维支撑。

```
   ██╗   ██╗██╗██╗   ██╗
   ██║   ██║██║██║   ██║
   ██║   ██║██║██║   ██║
   ╚██╗ ██╔╝██║╚██╗ ██╔╝
    ╚████╔╝ ██║ ╚████╔╝
     ╚═══╝  ╚═╝  ╚═══╝
```

---

## 命名

| 命名空间 | 代号 | 定位 |
|:--|:--|:--|
| **Banshee** | 报丧女妖 | 框架层 — 幕后驱动一切基础设施 |
| **Vivian** | 薇薇安 | 应用层 — 台前承载业务服务 |
| **Test** | — | CLI 工具集 — 命令自动发现 |

---

## 架构总览

```
                  ┌─────────────────────┐
                  │  Viv.Aspire.AppHost  │   Aspire 统一编排
                  └──────────┬──────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Viv.Aspire.Gateway  │   YARP 反向代理 + 限流 + 输出缓存 + JWT 解析透传
                  └──────────┬──────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
   ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
   │ Apex.Api  │  │DeepRed.Api│  │Herta.Api  │  ...  Vivian 服务（API / Worker / Link）
   │Apex.Worker│  │D.R.Worker │  │Herta.Link │
   └─────┬─────┘  └─────┬─────┘  └─────┬─────┘
         │              │              │
         └──────────────┼──────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
  ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
  │   Momo    │  │   Nana    │  │   Redis   │  Banshee 基础设施
  │ EF+Dapper │  │Wolverine  │  │ 缓存/分布式锁│
  │ 读写分离   │  │+RabbitMQ  │  │           │
  └───────────┘  └───────────┘  └───────────┘
```

### 请求链路（完整）

一次请求经 **网关 → 下游服务 → 数据层** 三级，身份与多租户上下文沿链路逐级验真：

```
客户端
 │ ① 携带 JWT（Authorization: Bearer <token>；WS 升级改走 ?access_token=<token>）
 ▼
Viv.Aspire.Gateway  ── 只解析不强制：无 token 也放行 ──
 │ ② CORS / OutputCache / RateLimiter
 │ ③ JwtBearer 解析 token
 │ ④ 剥离客户端伪造的身份来源：x-viv-* 头 + x-request-token + query 里的 tenantId/userId/appId 全部丢弃
 │ ⑤ token 有效 → 从 claims 回填 x-viv-* 头 + HMAC-SHA256 签名写 x-request-token（{unixSeconds}:{base64Sig}）
 ▼
下游服务（Apex.Api / Herta.Link …）
 │ ⑥ UseForwardedHeaders 信任 X-Forwarded-Proto/Host/For（避免 302 甩出网关直连下游）
 │ ⑦ UseAuthentication(JwtBearer) 验签 JWT → context.User（匿名服务 TokenOption=null 跳过）
 │ ⑧ VivContextMiddleware：验 x-request-token 签名通过才信任 x-viv-* 头 → IVivContext（AsyncLocal）
 │ ⑨ RequestFilterAttribute 参数校验 → Controller Action → VivExceptionFilter → VivApiResult
 ▼
数据层 Momo
 │ EF 全局租户过滤 / Dapper 自动追加 AND TenantId = @TenantId
 ▼
返回：{ Code, Message, Data }（HTTP 恒 200）
```

| 步骤 | 环节 | 职责 |
|:--|:--|:--|
| ① | 客户端 | 携带 `Authorization: Bearer <JWT>`；SignalR 升级请求无法带 Authorization 头，改用 `?access_token=<JWT>` |
| ② | 网关 | 限流、输出缓存、CORS |
| ③ | 网关 | JwtBearer 解析（`OnMessageReceived` 支持 `access_token` 查询参数，供 WS/SignalR 场景） |
| ④ | 网关 | 认证**前**剥离客户端可伪造的 `x-viv-appId/subjectId/userId/serviceName`、`x-request-token` 头，以及 query 里的 `tenantId/userId/appId` —— 身份只允许来自验签后的 token claims |
| ⑤ | 网关 | 认证通过后从 claims 回填 `x-viv-*` 头，HMAC-SHA256 签名写 `x-request-token`（载荷含 unix 时间戳，5 分钟过期）—— 防止绕过网关直连下游伪造头 |
| ⑥ | 下游 | 信任网关透传的 `X-Forwarded-Proto/Host/For`，避免 `UseHttpsRedirection` 把浏览器 302 甩出网关 |
| ⑦ | 下游 | JwtBearer 验签 JWT（`TokenOption=null` 的匿名服务不注册鉴权） |
| ⑧ | 下游 | `RequestTokenResolver.GetContextFromHeaders` 用 `EnvOption.InternalToken`（缺省回落 `TokenOption.SecretKey`）验 `x-request-token` 签名 + 时效；失败 → 视为无身份 |
| ⑨ | 下游 | 参数校验 → 业务 → 统一响应 |
| ⑩ | 数据层 | 多租户自动隔离（EF 查询过滤 / Dapper 追加租户条件），业务代码无需手写 |

**身份/租户上下文契约**（由网关验签后回填，下游验签才信任）：

| Header | 说明 |
|:--|:--|
| `x-viv-appId` | 客户端应用 ID（claims: `appId`） |
| `x-viv-subjectId` | 租户 ID = TenantId（claims: `tenantId`） |
| `x-viv-userId` | 用户 ID（claims: `sub`） |
| `x-viv-serviceName` | 服务名（网关填自己的 `EnvOption.ServiceName`） |
| `x-request-token` | `{unixSeconds}:{base64Sig}` — 对上面 4 个头 + 时间戳的 HMAC-SHA256（密钥 `EnvOption.InternalToken`，网关与所有服务同值；缺省回落 `TokenOption.SecretKey`）。下游验签 + ≤300s 时效通过才信任头组 |

**SignalR / WebSocket 链路**：

```
客户端 → /ws/{短名}/chat?access_token=<JWT>
  → 网关 OnMessageReceived 读 access_token → JwtBearer 验签 → 回填 x-viv-* + 签名 x-request-token
  → 下游 ChatHub.OnConnectedAsync：GetContextFromHeaders 验签取身份
  → AppId/SubjectId/UserId 任一无效 → Context.Abort()（不再信任客户端 query 直传的 tenantId/userId/appId）
  → 通过 → 加入连接池 + 用户组
```

---

## 项目结构

```
Viv/
├── src/
│   ├── Banshee/                          # 框架层
│   │   ├── Viv.Contracts/                # 基础接口与枚举（IVivContext、IDependency…）
│   │   ├── Viv.Delusion/                 # 通用工具 — 类型扫描、对象映射、加密、字符串
│   │   ├── Viv.Aoi/                      # DI 桥接 — VivLocator（MS DI ↔ Autofac）
│   │   ├── Viv.Engine/                   # 核心引擎 — 配置加载、AddViv 注册、启动扩展、统一状态码
│   │   ├── Viv.Log/                      # 日志 — Serilog / No-op，Seq 集成
│   │   ├── Viv.Momo/                     # 数据库 — EF Core + Dapper 混合、读写分离、多租户
│   │   ├── Viv.Nana/                     # 消息 — Wolverine + RabbitMQ 发布订阅、Saga
│   │   ├── Viv.Redis/                    # 缓存 — Redis 服务 + 分布式锁（自动续期）
│   │   ├── Viv.Sandrone/                 # 云能力 — JWT、S3、二维码
│   │   ├── Viv.Clockwork/                # 调度 — TickerQ 集成 + 仪表盘
│   │   ├── Viv.Echo/                     # 跨服务通信 — HTTP + gRPC
│   │   ├── Viv.Cli/                      # CLI 框架 — REPL + 命令自动发现
│   │   └── Viv.Forge/                    # 代码生成
│   │
│   ├── Vivian/                           # 应用层
│   │   ├── Viv.Entity/                   # 数据库实体（按领域分目录）
│   │   ├── Viv.Elysia/                   # 请求校验管线（RequestFilterAttribute、RequestValidator<T>）
│   │   ├── Viv.EventContracts/           # 跨服务消息定义
│   │   ├── Viv.Sdk/                      # 公共 SDK（gRPC 存根、DTO）
│   │   │
│   │   ├── Viv.Apex.Core|Api|Worker/     # Apex 领域
│   │   ├── Viv.DeepRed.Core|Api|Worker/  # DeepRed 领域
│   │   ├── Viv.Herta.Core|Api|Link/      # Herta 领域（Link = SignalR 实时通道）
│   │   ├── Viv.SakuMai.Api/              # SakuMai API（TickerQ 集成）
│   │   │
│   │   └── Viv.Aspire/
│   │       ├── Viv.Aspire.AppHost/        # Aspire 统一编排
│   │       ├── Viv.Aspire.Gateway/        # YARP 反向代理（路由自动生成）
│   │       └── Viv.Aspire.ServiceDefaults/ # OpenTelemetry、/health、服务发现、韧性
│   │
│   └── Test/
│       └── Viv.Test/                     # CLI 命令集
```

---

## 快速开始

### 环境依赖

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 16+ 或 SQL Server 2022+
- Redis 7.x
- RabbitMQ 3.x

### 启动

```bash
# 构建
dotnet build

# 一键启动全部服务（AppHost 编排，含网关）
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost

# 或单独启动
dotnet run --project src/Vivian/Viv.Apex.Api
dotnet run --project src/Vivian/Viv.Apex.Worker
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.Gateway

# CLI 测试工具集
dotnet run --project src/Test/Viv.Test
```

---

## 框架用法

### 一行动员：API / Worker / Gateway

每个服务只需三行，基础设施由框架一键装配：

```csharp
// ── API ──────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivApi("Viv Apex API", mvc => mvc.Filters.Add<RequestFilterAttribute>());
builder.RunVivApi(app => app.MapDefaultEndpoints());

// ── Worker（消息消费者 / 后台任务）──────────────────
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddVivWorker();
builder.Services.AddHostedService<Worker>();
builder.RunVivWorker();

// ── Gateway（YARP 反向代理）──────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivGateway(ignoreSslErrors: builder.Environment.IsDevelopment());
builder.RunVivGateway(app => app.MapDefaultEndpoints());
```

`AddVivApi` / `AddVivWorker` / `AddVivGateway` 自动完成：加载 `viv.config.json` → 装配 Autofac 容器 → `AddViv()` 注册全部子系统 → MVC / CORS / Swagger / 编码 → 中间件管线。`RunVivApi` 的 `configure` 回调可追加自定义端点（如 `app.UseTickerQ()`、`app.MapHub()`）。

### 配置：`viv.config.json`

每个服务项目根目录放一份 `viv.config.json`，框架按节驱动子系统装配：

```jsonc
{
  "EnvOption": {                          // 运行环境
    "Env": 0,                             // 0=Development 1=Test 2=PreRelease 3=Production
    "ServiceName": "viv.apex.api",        // 服务名（Nana 队列名 / 网关路由短名依赖它）
    "MachineId": 101,                     // 机器 ID（分布式 ID 生成）
    "InternalToken": "<32位随机hex>"       // x-request-token 签名密钥（网关与所有服务必须同一个值；缺省回落 TokenOption.SecretKey）
  },
  "DIOption": {                           // Service / Repository 自动扫描注册
    "ServiceImplementation": {
      "AssemblyName": "Viv.Apex.Core",
      "Namespace": "Viv.Apex.Core.Service",
      "BaseType": null,
      "ClassNameEndsWith": "Service",
      "ClassNameStartsWith": ""
    },
    "RepositoryImplementation": {
      "AssemblyName": "Viv.Apex.Core",
      "Namespace": "Viv.Apex.Core.Repository",
      "BaseType": null,
      "ClassNameEndsWith": "Repository",
      "ClassNameStartsWith": ""
    }
  },
  "CacheOption": {                        // 缓存
    "CacheProviderType": 1,               // 0=None 1=Redis
    "IsEnableMemoryCache": true,          // 是否启用进程内内存缓存
    "RedisOptions": {
      "RedisMode": 0,                     // 0=Standalone 1=Cluster 2=Sentinel
      "ConnectionString": "localhost:6379,password=***",
      "SentinelEndPoints": [],            // 哨兵节点列表（哨兵模式用）
      "SentinelMasterName": "MasterRedisNode",
      "Password": "***",
      "DefaultDatabase": 0,
      "MaxDbIndex": 12,                   // 可用 DB 范围 0~12（多租户按租户分库）
      "SelectorType": 0,                  // 0=None 固定默认库 1=KeyHash 按 key 哈希分库
      "AllowAdmin": true,
      "AbortOnConnectFail": false,
      "ConnectTimeout": 5000,
      "SyncTimeout": 5000,
      "KeepAlive": 60
    }
  },
  "LogOption": {                          // 日志
    "LogType": 1,                         // 0=None 1=Serilog
    "IsUseSeq": true,
    "SeqUrl": "https://seq.example.com",
    "SeqApiKey": "***"
  },
  "DatabaseOption": {                     // Momo 数据库
    "DatabaseSource": 0,                  // 0=SqlServer 1=PostgreSQL
    "IsReadWriteSplit": false,
    "MasterConnectionString": "Server=...;Database=viv;...",
    "SlaveConnectionStrings": [],
    "Timeout": 30,
    "EntityTypeOptions": [{               // 实体自动扫描
      "AssemblyName": "Viv.Entity",
      "Namespace": "Viv.Entity.Database.Apex",
      "BaseType": "Viv.Momo.Interface.IEntity, Viv.Momo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
    }]
  },
  "NanaOption": {                         // Nana 消息（Wolverine + RabbitMQ）
    "Host": "localhost",
    "Port": 5672,
    "UserName": "viv",
    "Password": "***",
    "VirtualHost": "VivNet",
    "RetryCount": 3,
    "ConsumerTypes": [],                  // 消费者类型扫描规则（ClassNameEndsWith: "Consumer"）
    "SagaDatabaseSource": 0,              // Saga 持久化库类型（0=SqlServer 1=PostgreSQL）
    "SagaConnectionString": null          // 不配则不启用 Saga 持久化
  },
  "TokenOption": {                        // JWT
    "TokenType": 0,                       // 0=Jwt
    "SecretKey": "***",                   // ≥32 字节，所有签发/验证 token 的服务（含网关）必须一致
    "ExpireMinutes": 120,
    "Issuer": "viv.system.net",
    "Audience": "viv.system.net"
  },
  "TickOption": null,                     // Clockwork 调度（TickerQ），不使用则为 null
  "EchoOption": {                         // 跨服务通信
    "EnableHttp": true,
    "EnableGrpc": true
  },
  "S3Option": {                           // 对象存储（S3 兼容，RustFS 等）
    "Endpoint": "https://s3.example.com",
    "UseHttps": true,
    "Port": 443,
    "AccessKey": "***",
    "SecretKey": "***",
    "Region": "us-east-1",
    "UploadBucket": "vivbucket",
    "UploadPresignExpireSeconds": 900,
    "DownloadPresignExpireSeconds": 900
  }
}
```

### 数据库 Momo

`IMomoDbContext` 是统一数据访问入口，EF Core 与 Dapper 按阈值混合使用（小操作走 EF 变更追踪，大查询走 Dapper 纯 SQL）。

```csharp
public class UserService : IUserService
{
    private readonly IMomoDbContext _db;
    public UserService(IMomoDbContext db) => _db = db;

    public async Task<VivApiResult<User>> GetByIdAsync(long id)
    {
        var user = await _db.FindAsync<User>(id);           // 按 Id 查询（自动租户隔离）
        return user is not null
            ? VivApiResult<User>.Success(data: user)
            : VivApiResult<User>.Failed("用户不存在");
    }
}
```

内置能力：

- **读写分离** — 写走主库、读随机选从库（`EFAppContext` 锁定读写方向）
- **多租户隔离** — 实现 `ITenant` 的实体自动附加租户过滤，跨租户读取被框架拦截
- **软删除** — 实现 `ISoftDelete` 即可
- **事务** — `BeginTransactionAsync` / `CommitTransactionAsync` / `RollbackTransactionAsync`
- **建表** — `SyncTableAsync` 按实体自动同步表结构

### 消息 Nana（Wolverine + RabbitMQ）

发布订阅语义：每条事件进 fanout 交换机 `{EventName}Exchange`，每个订阅服务持一条独立队列 `{EventName}Queue.{ServiceName}` 各收一份；"只执行一次"由业务侧拿 Redis 分布式锁保证。

```csharp
// 发布（消息类需继承 NanaEvent）
public interface IVivEventPublisher
{
    Task<bool> PublishAsync<T>(T content, CancellationToken ct = default) where T : NanaEvent;
    Task<bool> PublishDelayAsync<T>(TimeSpan delay, T content, CancellationToken ct = default) where T : NanaEvent;
}

// 消费（继承 VivConsumer<T>，实现业务逻辑）
public class UserCreatedConsumer : VivConsumer<UserCreated>
{
    public UserCreatedConsumer(ILoggerContract logger) : base(logger) { }

    public override async Task<SubscribeResult> ReceiveMessageAsync(
        NanaEnvelope<UserCreated> message, CancellationToken ct = default)
    {
        // ... 业务处理（多租户上下文随信封透传，message.Context）
        return SubscribeResult.Success();              // 成功确认
        // return SubscribeResult.Fail(isRequeue: true, "重试");  // 要求重投
    }
}
```

消费失败自动按 `RetryCount × 1s` 重试，耗尽后进死信队列；租户上下文（`NanaEnvelope<T>.Context`）随消息透传到下游，消费侧多租户隔离不受影响。Saga 状态机可基于 EF 持久化（配 `SagaConnectionString` 自动启用）。

**消费并发/预取调优**：框架默认每通道预取 **20** 条（低于 Wolverine 原生 100，降低崩溃重投放大）、队列用 **Quorum** 类型（多副本防丢消息）。需要吞吐时给消费者标特性覆盖：

```csharp
[NanaConsumer(ConsumerCount = 4, PrefetchCount = 200, MaximumParallelMessages = 32)]
public class UserCreatedConsumer : VivConsumer<UserCreated> { ... }
```

`ConsumerCount` = 队列消费通道数（**>1 会丢失同队列内的严格顺序**，多实例时总数 = 通道数 × 实例数）；`PrefetchCount` = 每通道未确认上限；`MaximumParallelMessages` = 端点最大并行。

### 统一响应

所有 API 返回 `VivApiResult` 信封，**HTTP 状态码恒为 200**，业务结果由 `Code` 表达：

```json
{ "code": 200, "message": "请求处理成功", "data": { ... } }
```

| 区间 | 含义 |
|:--|:--|
| `2xx`（如 `200`、`201`） | 成功类 |
| `-2xx`（如 `-200` 通用业务错误、`-201` 缺参、`-202` 格式错） | 参数与基础业务拦截 |
| `-4xx`（如 `-400` Token 空、`-401` Token 异常、`-404` 资源不存在） | 鉴权 / Token / 身份 |
| `-6xx`（如 `-601` 无权限、`-602` 越权访问） | 功能 / 数据 / 渠道权限 |
| `-5xx`（如 `-500` 兜底、`-501` 数据库、`-502` 缓存、`-503` 消息队列） | 系统 / 中间件异常 |

细分业务错误统一用 `-200`，自定义提示文案即可，不新增业务专属枚举。

### 多租户与网关

**上下文头**（由网关验签后回填，下游验签才信任）：

| Header | 说明 |
|:--|:--|
| `x-viv-appId` | 客户端应用 ID |
| `x-viv-subjectId` | 租户 ID（TenantId） |
| `x-viv-userId` | 用户 ID |
| `x-viv-serviceName` | 服务名 |
| `x-request-token` | HMAC-SHA256 签名（`{unixSeconds}:{base64Sig}`，5 分钟过期） |

`VivContextMiddleware` 解析头部（或 JWT claims）填充 `IVivContext`，数据层据此自动做租户过滤。完整链路见上节**请求链路**。

**网关路由自动生成**（零手写 JSON）：从 Aspire 服务发现为每个服务生成 3 条路由 + 1 个集群：

| 路由 | 目标 | 用途 |
|:--|:--|:--|
| `/api/{短名}/{**catch-all}` | `/api/{**catch-all}` | 标准 API |
| `/docs/{短名}/{**catch-all}` | `/{**catch-all}` | Scalar 文档（经网关透出） |
| `/ws/{短名}/{**catch-all}` | `/{**catch-all}` | WebSocket / SignalR |

网关只**解析不透传强制**鉴权：JWT 有就验签回填 `x-viv-*` 头（先剥离客户端伪造头与 query 身份参数，再 HMAC 签名 `x-request-token` 防绕过直连），无则放行；鉴权由下游服务 `[Authorize]` 自己控制。签名密钥取 `EnvOption.InternalToken`（缺省回落 `TokenOption.SecretKey`）。限流策略在 `viv.ratelimit.json` 热重载。

### CLI 命令

引用 `Viv.Cli`，实现 `AsyncCommand` 并标注 `[VivCommand]`，自动发现注册：

```csharp
[VivCommand("migrate", "执行数据库迁移")]        // 单命令
[VivCommand("clear, cl", "清除屏幕")]            // 多别名
public class Cmd_Migrate : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext ctx) { ... }
}
```

交互输入与格式化输出：

```csharp
var name = InputMagic.GetInput("请输入名称");         // 必填
if (InputMagic.Confirm("确认?")) { ... }             // y/n

Out.PrintlnSuccess("完成");
Out.PrintlnFormatJson(someObject);                   // JSON Panel
```

---

## License

MIT
