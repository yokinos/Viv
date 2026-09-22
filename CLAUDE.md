# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the entire solution
dotnet build

# Run the Aspire AppHost (orchestrator that launches all services)
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost

# Run a specific API
dotnet run --project src/Vivian/Viv.Apex.Api

# Run a specific Worker
dotnet run --project src/Vivian/Viv.Apex.Worker
```

## Architecture

The solution splits into two top-level namespaces: **Banshee** (framework) and **Vivian** (application). A third namespace, **Test** (`src/Test/`), holds per-project unit tests. CLI tooling lives in `Viv.Cli`.

---

### Banshee — the Viv framework (`src/Banshee/`)

| Project | Role |
|---|---|
| `Viv.Contracts` | Base interfaces (`IVivContext`, `IDependency`) and shared enums；**本地事件契约** `IVivLocalEventBus` / `IVivLocalEventScope` / `LocalEvent`（空标记基类）/ `IVivLocalEventHandler<TEvent>` / `LocalEventHandler<TEvent>`（零 Nana 依赖，业务 Core 直接引它写处理器）；**分布式锁契约** `IDistributedLock` —— 6 个方法的锁标识**统一是 `object key`**（不再有 `string lockKey` / `object key` 之分）：传 `string` 原样作 Redis Key（前缀调用方自己拼），其余类型由实现 `DistributedLockAccessor.GenerateLockKey` 归一化成 `lock:{...}` |
| `Viv.Delusion` | Utility library — `TypeScanMagic` (assembly type scanning), `ObjectMapper` (Emit + Expression-based), encryption, common extensions |
| `Viv.Aoi` | DI bridge — `VivLocator` wraps both MS DI and Autofac `ILifetimeScope`; static service resolution for non-injection scenarios |
| `Viv.Engine` | **Core wiring hub** — `VivEngine.LoadVivConfig(builder.Configuration)` binds the `VivOptions` node from appsettings.json into `VivOptions`; `VivRegister` wires every Banshee subsystem into DI via `AddViv()`; provides `VivApiExtensions` / `VivWorkerExtensions` / `VivStartGatewayExtensions` for one-liner startup；**本地事件总线实现** `LocalEvents/`（`LocalEventBus` / `LocalEventHandlerInvoker<T>` / `LocalEventRegistration` / `LocalEventScope`）+ 两个触发点 `LocalEventFlushFilterAttribute`、`LocalEventFlushMiddleware`（**同目录**，本地事件一个文件夹全包）。⚠️ **目录／命名空间是复数 `LocalEvents`**：事件基类叫 `LocalEvent`，若目录同名，`Viv.Engine.LocalEvent` 这个命名空间会在 `Viv.Engine` 里把类型 `LocalEvent` 遮住，`LocalEvent` 一律解析成命名空间（CS0118，实测踩过） |
| `Viv.Log` | Logging — Serilog or no-op backend, configurable per `LogType`; Seq integration |
| `Viv.Momo` | Database — `IMomoDbContext` backed by **EF Core + Dapper** hybrid; read/write connection routing via `EFAppContext`; supports PostgreSQL and SQL Server；**实体审计**（`ICreatedAt` / `ICreatedBy` / `IUpdatedAt` / `IUpdatedBy` 四个单字段能力接口，逐个 opt-in，由 `MomoDatabase` 自动盖章，见 `### Entity audit`）；**建表 DDL**（`Sync/SchemaSynchronizer` 按实体生成 CREATE/ALTER，双方言，见 `### Schema sync`）；**缓存基类** `Base/DataAccessCacheBase<T>`（Cache-Aside，8 个业务仓储继承）—— **锁走 `IDistributedLock`，缓存读写走 `IRedisService`**，两条路径 Redis 故障都 catch 后回源数据库（锁那侧 Redis 故障被包成 `DistributedLockException`，得单独接一次）；取锁用 `AcquireLockWithRetryAsync` 并把参数压到 `maxRetryCount: 3, baseDelay/maxDelay: 20ms`（用默认的 5 次指数退避 = 约 3 秒，缓存击穿场景等不起） |
| `Viv.Nana` | Messaging — **两条平行的线**：① 跨进程 `NanaEvent` + `IVivEventPublisher` / `NanaEventPublisher` / `VivConsumer<T>`（Wolverine + RabbitMQ，fanout）② 进程内本地队列 `NanaLocalEvent` + `IVivLocalEventPublisher` / `NanaLocalEventPublisher` / `VivLocalConsumer<T>`（Wolverine local queue，点对点，两族互不引用）；Saga support with EF Core state persistence |
| `Viv.Outbox` | **发件箱（事务性消息投递）+ Inbox（消费端幂等）** — `IVivOutbox` / `OutboxStore`（Scoped，入队走 `ExecuteSqlAsync` 并入业务事务）+ `OutboxDispatcher`/`OutboxWorker`（后台投递，原子认领）+ 手写 SQL（**一次都不经过 EF**，表 `VivOutboxMessage`）。解决「写库 + 发消息」不原子：**写和待发消息进同一个本地事务**，投递交给后台。Inbox 侧 `IVivInbox` / `InboxStore`（表 `VivInboxMessage`）+ `InboxDispatcher`（按保留期清理，独立循环）。见下 |
| `Viv.Redis` | Redis cache — `IRedisService` with pluggable DB allocation (`DbSelectorType`)。访问失败抛 `VivConnectionException(Redis)`（API 过滤器 `-502`，客户端只回固定文案）；`DataAccessCacheBase` 读路径 catch 后回源数据库，**取锁也走 catch 后回源**（见 `Viv.Momo` 行）。写仍抛。锁续期后台任务仍只记日志后停止 |
| `Viv.Sandrone` | Cloud integrations — JWT `ITokenService`/`JwtTokenService`（TokenOption 对称密钥）、S3 `IS3Service`/`VivS3Service` |
| `Viv.Echo` | Service-to-service communication + **框架级 gRPC 宿主**（`Viv.Echo.Grpc`）— HTTP + gRPC 客户端 `VivGrpcInterceptor`/`AddVivGrpcClient`（支持服务发现；注入 x-viv-* 含 holder-id 并纳入签名）、服务端 `VivGrpcServerInterceptor`（验签后水合 `IVivContext` 并 `SetHolderId`）/`AddVivGrpcServer`/`AddVivGrpcKestrel`/`VivGrpcDiscovery`（自动发现 `[BindServiceMethod]` 实现类 + 注册 + 反射映射；REST + gRPC 分端口，见下） |
| `Viv.Clockwork` | Background scheduling — `TickerQ` integration for cron/interval job execution with dashboard；**任务基类** `VivTickerJobBase`（子类 `[TickerFunction]` 入口走受保护的 `RunAsync`）+ `VivTickerJob.ExecuteAsync`：包一层 `IVivLocalEventScope`（成功 Flush / 失败 Discard），并硬校验开工前已有租户或系统租户快照 —— 定时任务不必自己管本地事件分发 |
| `Viv.Cli` | **CLI framework** — `VivCliHost` (REPL loop + Spectre.Console.Cli `CommandApp`); `[VivCommand]` auto-discovery; built-in `Cmd_Clear`; `Out` (formatted output) and `InputMagic` (interactive input) utilities |
| `Viv.Forge` | **Source generator base library** — `VivSourceGenerator<TInfo>`（增量管线基类：候选筛选→语义提取→Collect→产出，异常兜底诊断）、`VivAttributeGenerator<TAttribute,TInfo>`（特性驱动基类，按全名匹配特性）、`SourceBuilder`（缩进/using 去重/auto-generated 头）、`SourceGenHelpers`（特性参数读取/标识符清理/字符串转义）。具体生成器标注 `[Generator]` 并继承基类，挂载到目标项目 `<ProjectReference OutputItemType="Analyzer">` |

---

### Vivian — the application layer (`src/Vivian/`)

**Domain projects (DDD-style per bounded context):**

| Domain | Core | Api | Worker |
|---|---|---|---|
| **Apex** | `Viv.Apex.Core` | `Viv.Apex.Api` | `Viv.Apex.Worker` |
| **DeepRed** | `Viv.DeepRed.Core` | `Viv.DeepRed.Api` | `Viv.DeepRed.Worker` |
| **Herta** | `Viv.Herta.Core` | `Viv.Herta.Api` + `Viv.Herta.Link` (SignalR) | — |
| **SakuMai** | — | `Viv.SakuMai.Api` (TickerQ integrated) | — |

**Shared/cross-cutting:**

| Project | Role |
|---|---|
| `Viv.Entity` | EF entity classes organized by domain (e.g. `Database/Apex/`) |
| `Viv.Elysia` | Request validation pipeline — `RequestFilterAttribute`（`RequestParameterValidator` 校验 `IApiRequest` 子类）+ `ApiRequestBase` 请求基类; **操作日志** — `OperationLogFilterAttribute` + `ElysiaLogContextAccessor`（AsyncLocal 预置容器，见下） |
| `Viv.EventContracts` | Shared message/event class definitions for inter-service messaging |
| `Viv.Generators` | 应用专用源生成器（netstandard2.0，继承 `Viv.Forge` 基类，字符串全名匹配特性） |
| `Viv.Meta` | 生成代码宿主（net10.0，挂 Viv.Forge + Viv.Generators 两个 Analyzer，业务引它拿生成类型） |
| `Viv.ServiceProxy` | **业务侧 gRPC 实现层（服务自行 ProjectReference 挂 proto/示例，框架级装配走配置驱动）**：`Protos/tenant_grpc.proto` 契约（4 RPC 覆盖 unary/server-streaming/client-streaming/bidi）+ `Examples/TenantGrpcService` 示例实现 + `TenantGrpcClientDemo` 客户端用法示意；框架级能力（`AddVivGrpcServer`/`AddVivGrpcClient`/服务端租户拦截器/`AddVivGrpcKestrel`）已收进 `Viv.Echo`，宿主配 `EchoOption.GrpcOption` 后示例经 `VivGrpcDiscovery` 自动发现托管 |

**REST + gRPC 明文端口约束**：gRPC 需要 HTTP/2。明文下 `Http1AndHttp2` 只认 TLS/ALPN，不认 h2c prior-knowledge 前缀（Grpc.Net.Client 明文即发前缀）→ 回 `HTTP_1_1_REQUIRED`；严格 `Http2` 会把 HTTP/1.1 REST 打挂（400）。故 REST 与 gRPC 必须**分开端口**。**配置驱动（宿主零手工接线）**：appsettings.json 的 `VivOptions.EchoOption.GrpcOption`（`EnableServer` + `Port`）启用时，`AddVivApi` 自动调 `AddVivGrpcKestrel(port)`（`Viv.Echo.Grpc`：gRPC 端口绑严格 HTTP/2，并把 urls——`--urls`/`ASPNETCORE_URLS`/launchSettings——显式 `Listen` 回 HTTP/1.1；显式 `Listen` 会顶掉 urls 生成的端点，必须重绑，无 urls 回落 Kestrel 默认 5000；声明端口即自动调 `AddVivGrpcServer` 含租户上下文恢复拦截器）+ `VivGrpcDiscovery` 自动发现注册 gRPC 服务，`RunVivApi` 在 `MapControllers()` 后自动 `MapGrpcService<T>`。**自动发现约定**：grpc_csharp_plugin 生成的基类（如 `TenantGrpcServiceBase`）**不继承 `ServiceBase`**，以基类上的 `[BindServiceMethod]` 特性沿基类链判定（`VivGrpcDiscovery.FindServices` 先 `TypeScanMagic.ForceLoadReferencedAssemblies()` 强制加载懒加载程序集）。**Apex.Api 已配 7001、Herta.Api 配 7002**，示例 `TenantGrpcService` 自动托管（保留 ServiceProxy ProjectReference）；非 gRPC 宿主服务 `EchoOption.GrpcOption: null`（死属性 `EnableGrpc` 已移除）。测试用严格 Http2 的 Kestrel in-process server 验证。 |

### Aspire orchestration (`src/Vivian/Viv.Aspire/`)

| Project | Role |
|---|---|
| `Viv.Aspire.AppHost` | .NET Aspire orchestrator — launches all services with dependency ordering |
| `Viv.Aspire.Gateway` | **YARP** reverse proxy — 由框架层 `VivStartGatewayExtensions` 启动；限流、输出缓存、JWT 解析（`TokenOption` 对称密钥，**只解析不强制**）、认证后向透传 `x-viv-*` 上下文头（appId/subjectId(=TenantId)/userId/serviceName/holder-id）；路由从 Aspire 服务发现自动生成，下游服务自行鉴权 |
| `Viv.Aspire.ServiceDefaults` | OpenTelemetry tracing/metrics, `/health` + `/alive` endpoints, service discovery, HTTP resilience |

### Test (`src/Test/`)

Unit test suites, one per framework project — `Viv.Delusion.Tests`、`Viv.Engine.Tests`、`Viv.Momo.Tests`、`Viv.Nana.Tests`、`Viv.Outbox.Tests`、`Viv.Redis.Tests`、`Viv.Sandrone.Tests`。CI（`.github/workflows/dotnet.yml`）会跑全量测试并上报覆盖率。业务层的测试项目（`Viv.Elysia.Tests`、`Viv.Herta.Tests`、`Viv.ServiceProxy.Tests`）在 `src/Vivian/` 各自项目旁。

#### `Viv.Fakes` —— 测试替身集中在此，**不得散落到各测试项目**

`src/Test/Viv.Fakes/`：全仓**唯一**允许手写替身（mock/fake/stub）的地方。各测试项目 `ProjectReference` 它，项目内只留「**被测对象 + 探针 + 测试数据**」。硬性要求：**实现不能分散在各测试项目** —— 想改一处替身行为，只应该有一个文件要改。

- **不是测试项目**：csproj 写死 `<IsTestProject>false</IsTestProject>` 且**刻意不带** `Microsoft.NET.Test.Sdk` / `xunit` / `coverlet.collector`（替身没有一个用到 xUnit 类型）→ `dotnet test Viv.slnx` 静默跳过它，不会报「没有可用测试」。带测试 SDK 反而会被当测试宿主去跑。
- **不参与覆盖率**：程序集级 `ExcludeFromCodeCoverage`（实测 coverlet 遵守，cobertura 里 `Viv.Fakes` 类数 = 0）+ CI 侧 reportgenerator `-assemblyfilters:"-Viv.*.Tests*;-Viv.Fakes*"` 双保险，覆盖率汇总脚本里另有 `SKIP_PREFIXES = ('Viv.Fakes',)`。
- **文件组织按接口族**：`Logging.cs`（`RecordingLogger : ILoggerContract`）、`Context.cs`（`TestContext : IVivContext` + `TestContextAccessor : IVivContextAccessor`）、`Messaging.cs`（`RecordingEventPublisher` / `RecordingLocalEventPublisher` / `RecordingDistributedLock`）、`Caching.cs`（`TestBucket` + `CacheSut : DataAccessCacheBase<TestBucket>` + `CacheDoubles`）、`Audit.cs`（`MomoAuditSut : MomoDatabase`，暴露 `protected` 的审计填充方法）、`Transactions.cs`（`KernelStub : ITransactionKernel`）、`Outbox.cs`（`StubOutboxRepository : IOutboxRepository`）、`Hosting.cs`（`StubHost` / `FakeRequest : IApiRequest` / SignalR 四件套 / `StubConnectionPool`）、`Grpc.cs`（三个流替身）、`Proxies.cs`（`TestProxy : DispatchProxy` + `NopProxy`）、`XUnitTestMagic.cs`（`CreateOptions<T>`）。
- **只有两个替身是 `internal`**：`KernelStub` / `StubOutboxRepository` 要桩的接口（`ITransactionKernel` / `IOutboxRepository`）本身是 internal，而 **public 类实现 internal 接口是 CS0061**，故替身只能 internal，靠 `InternalsVisibleTo` 放给 `Viv.Engine.Tests` / `Viv.Outbox.Tests`（`Viv.Fakes.csproj` 里那两条 IVT 就是为它俩开的，**不要为了让替身「哪都能用」而全开**）。生产侧 `Viv.Engine` / `Viv.Outbox` 各反向加了一条 `InternalsVisibleTo Include="Viv.Fakes"`。
- **⚠️ `TestProxy` / `NopProxy` 不能加 `sealed`**：`DispatchProxy.Create<T, TProxy>()` 要**派生**动态类型，`TProxy` 必须可跨程序集继承。这两个类是全仓唯一不能用 `sealed` 的替身。
- **`XUnitTestMagic.CreateOptions<T>` 原来住在生产程序集 `Viv.Contracts`，已搬进这里** —— 测试设施不该跟着业务代码发布。
- **留在原地的（判据：被测对象的一部分 / 按程序集名被扫描 / 纯测试数据，搬了会静默变红）**：
  - `EngineTestEnv` + `VivEngineStaticStateCollection`（`Viv.Engine.Tests`）—— 带 `[CollectionDefinition(DisableParallelization = true)]`，是**该项目的并行度策略**。
  - Nana 的 `VivConsumer<T>` / `VivLocalConsumer<T>` 子类 —— `NanaRegisterTests` 写死 `AssemblyName = "Viv.Nana.Tests"` 并断言扫到它们。
  - `TestEntities.cs`（`Viv.Momo.Tests`）—— `TenantFilterTests` 写死 `AssemblyName`/`ClassNameEndsWith` 让 EF 扫，搬走 EF 扫不到实体、两条 `EfOnModelCreating_*` 静默变红。`ExposedEfAppContext` 同因。
  - Engine 的探针服务与 `ConsumerStub` —— 是**扫描算法的输入数据**，搬走会污染扫描范围。
  - `SampleController`（Elysia，带 `[OperationLog]` 的探针控制器）、各测试事件 POCO、`TestPayload`、`GrpcTestServer`（真 in-process Kestrel 夹具）。
  - `Viv.Apex.Tests` / `Viv.DeepRed.Tests` 目前是空项目（0 个 `.cs`，`dotnet test` 报「没有可用测试」，属既有状态）。
- **新增测试项目时**：直接 `ProjectReference ..\..\Test\Viv.Fakes\Viv.Fakes.csproj`（`src/Vivian/` 下的项目多退一级为 `..\..\Test\...`），不要在本项目里另起替身。

---

## Key Patterns

### Startup: one-liner API & Worker & Gateway

Do **not** copy `Program.cs` boilerplate. Use the framework extension methods:

```csharp
// ── API ──────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivApi(new ApiInitSetting("Viv XXX API"), mvc => mvc.Filters.Add<RequestFilterAttribute>());
builder.RunVivApi(app => app.MapDefaultEndpoints());

// ── Worker ────────────────────────────────────────
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddVivWorker();
builder.Services.AddHostedService<Worker>();
builder.RunVivWorker();

// ── Gateway（YARP 反向代理）─────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivGateway();                      // 读 appsettings.json 的 VivOptions + viv.ratelimit.json；路由从 Aspire 服务发现自动生成
builder.RunVivGateway(app => app.MapDefaultEndpoints());
```

- `AddVivApi` / `AddVivWorker` handle config load, Autofac setup, `AddViv()`, MVC/filters, CORS, Swagger, and encoding registration.
- `RunVivApi` handles Build → VivLocator → **`UseForwardedHeaders`**（信任网关透传的 `X-Forwarded-Proto/Host/For`，避免 `UseHttpsRedirection` 把浏览器 302 甩出网关直连下游）→ Swagger UI (dev) → middleware pipeline → Run. Accepts an `Action<WebApplication>? configure` for custom endpoints (`UseTickerQ()`, `MapHub()`, etc.).
- `RunVivWorker` handles Build → VivLocator → Run.
- `AddVivGateway` handles config load、Autofac、`AddViv()`、JWT 解析（读 `TokenOption` 对称密钥，**只解析不强制**）、CORS、OutputCache、RateLimiter、`AddReverseProxy().LoadFromMemory(...)`（路由/集群从 Aspire 服务发现自动生成）；`RunVivGateway` 管道：Build → VivLocator → WebSocket → CORS → OutputCache → RateLimiter → Authentication → Authorization → **上下文头透传**（先剥离客户端伪造的 `x-viv-*` 头与 `x-request-token`，再剥离 query 里客户端直传的身份参数 `tenantId/userId/appId`，认证后从 token claims 回填 `x-viv-appId`/`x-viv-subjectId`(=TenantId)/`x-viv-userId`/`x-viv-serviceName`/`x-viv-holder-id`（网关当前 `LockHolderContext`）并 HMAC 签名写 `x-request-token`）→ `MapReverseProxy` → Run。**路由不带 AuthorizationPolicy** —— 网关不鉴权，下游服务自己用 `[Authorize]` 控制。
- **SignalR/WS 认证**：网关 JwtBearer 支持 `access_token` 查询参数认证（`JwtBearerEvents.OnMessageReceived`，SignalR 升级请求无法带 `Authorization` 头）；客户端经 `/ws/{短名}/...` 需带有效 JWT 才能获得身份（经 `x-viv-*` 头下传），下游 hub（如 `ChatHub`）读 `RequestTokenResolver.GetContextFromHeaders` 取已验身份，**无身份即 `Context.Abort()`**——不再信任客户端 query 直传的 tenantId/userId/appId。
- **`x-request-token` 防重放**：签名载荷含 unix 时间戳（token 格式 `{unixSeconds}:{base64Sig}`），下游验签时校验 ≤300s，超时/旧格式（无冒号）一律拒绝——截获签名头组也不能无限期重放冒充。
- **头部契约/白名单集中存放**：上下文头名（`x-viv-appId`/`x-viv-subjectId`/`x-viv-userId`/`x-viv-serviceName`/`x-viv-holder-id`/`x-request-token`）**单一来源定义在 `VivHeaderContract`（`Viv.Contracts`，gRPC/HTTP 跨层共用，Echo/ServiceProxy 拦截器读它）**，`VivRunDefine`（`Viv.Engine`）以别名引用保持既有调用点不变；HTTP 状态码白名单仍在 `VivRunDefine`。网关/`RequestTokenResolver`/`VivApiResult` 跨层共用同一来源。
- **holderId 跨进程传播**：`LockHolderContext` 仍是进程内 AsyncLocal；跨进程走签名头 `x-viv-holder-id`（HMAC 载荷第 5 个字段，与身份头同组）。**网关是信任根**：`RequestTrackMiddleware` 在 `ServiceType=Gateway` 时一律 `GenerateHolderId()`，认证后回填并签名。下游 API 验签通过且带头才 `SetHolderId(上游)`，否则本进程生成——客户端无密钥伪造不了。gRPC 客户端 `VivGrpcInterceptor` 注入当前 holderId 并纳入签名，服务端 `VivGrpcServerInterceptor` 验签后 `SetHolderId`。`NanaEnvelope.Context.HolderId` 由发布端从 `LockHolderContext` 盖章，Worker `VivConsumer` 优先用它（不再用 `TraceId` 当 holder）。
- **内部签名密钥 `EnvOption.InternalToken`**：`RequestTokenResolver.GetInternalSecret()` 只取 `VivEngine.VivOptions?.EnvOption?.InternalToken`（appsettings.json 的 `VivOptions.EnvOption` 节点，网关与**所有服务必须配同一个值**，32 位随机 hex），**不回落** `TokenOption.SecretKey`。未配 InternalToken 时不验签，按原行为信任身份头——只用于无租户数据场景；**holderId 除外**（无密钥一律本进程生成，不信任客户端头）。
- **路由自动生成**：`VivGatewayRouteBuilder.Build()` 从 Aspire 注入的 `services__*` 环境变量（`WithReference`）为每个服务生成 3 条路由 + 1 个集群，**零手写 JSON**（无 `viv.yarp.json`）。新增 API 只需在 AppHost 加 `WithReference`：
  - `/{短名}/api/{**catch-all}` → `/api/{**catch-all}`（标准 API，如 `/apex/api/...`，`PathPattern` 匹配替换吃掉短名前缀）
  - `/docs/{短名}/{**catch-all}` → `/{**catch-all}`（Scalar 文档）
  - `/ws/{短名}/{**catch-all}` → `/{**catch-all}`（SignalR / WebSocket 透传）
  - **短名 = 服务名 `split('-')[1]`**（`viv-apex-api` → `apex`）；第二段冲突时（`viv-herta-api` 与 `viv-herta-link` 都是 `herta`），`-api` 保留基础短名，其余服务拼接剩余段（`herta`+`link` → `hertalink`），保证集群 ID 唯一。
- **下游自鉴权**：`AddVivApi` 自动注册 JwtBearer（读 appsettings.json 的 `VivOptions.TokenOption`），控制器用 `[Authorize]` 逐个控制；`TokenOption` 为 `null`（如 hertalink）则跳过注册保持匿名——此时 `RunVivApi` **不调用** `UseAuthentication/UseAuthorization`（否则匿名服务首请求抛 `IAuthenticationSchemeProvider` 无法解析）。网关只解析透传，不拦截未登录请求。
- **网关代理文档**：`/docs/{短名}/{**catch-all}` 路由（如 `/docs/apex/scalar/`）把各服务 **Scalar** 文档经网关透出，欢迎页服务标签即指向此（不跳服务自身地址）。前提：Scalar.AspNetCore ≥2.16 生成的 HTML 用相对路径（`openapi/v1.json`、`./scalar.aspnetcore.js`）且自带子目录 basePath 计算，`PathPattern: "/{**catch-all}"` 去掉 `/docs/{短名}` 即可；链接需带尾斜杠 `/scalar/`，否则下游 302 后浏览器会请求网关根路径 `/scalar/`。路由自动生成，欢迎页零映射。
- **JWT SecretKey ≥ 32 字节**：IdentityModel 8.x 的 HS256 强制要求 ≥256 bit（`IDX10720`），且 `TokenOption` 必须在**所有会签发/验证 token 的服务间保持一致**（含网关）。
- `AddServiceDefaults()` and `MapDefaultEndpoints()` are **caller-side** Aspire concerns; the framework does not reference Aspire.

### Configuration: `VivOptions` node in `appsettings.json`

Every API and Worker project carries a `VivOptions` node in its `appsettings.json`. `VivEngine.LoadVivConfig(builder.Configuration)` binds it via `configuration.GetSection("VivOptions").Get<VivOptions>()`（MS ConfigurationBinder）and sets the static `VivEngine.VivOptions` snapshot for runtime consumers. Sub-sections drive all subsystem wiring:

> **DI 注册（推荐的消费方式）**：三个 starter 在 `LoadVivConfig` 之后调用 `builder.AddVivConfig()`（`VivConfigLoader`，静态实例模式），把每个非 null 子配置**双注册**进 DI —— `AddSingleton(T)` + `AddSingleton(IOptions<T>)`。另有 `AddVivConfigFromConfiguration()` 走 `Configure` + `IOptionsMonitor` 支持热更新，但**只注册 `IOptions<T>`、不注册 `T` 本身**（注入具体类型会解析失败）。领域服务/中间件/拦截器一律**构造注入**配置（如 `IOptions<NanaOptions>`、`VivInternalTokenOptions`），不要再走静态通道；`VivEngine.VivOptions` 静态快照只保留给 Engine 自身的静态路径（如 `RequestTokenResolver.GetInternalSecret`）。**`VivConfigRegistry` 已无任何调用者**（2026-09-14 全部改为 DI），仅类定义留存。

> **环境变量覆盖（走标准配置链后生效）**：`VivOptions__*` 可覆盖任意节点（如 `VivOptions__EnvOption__InternalToken`、`VivOptions__TokenOption__SecretKey`）——此前 `LoadVivConfig` 绕过 IConfiguration 时 env 对 viv 配置无效。各服务 appsettings.json 目前仍直接携带凭据（迁移现状，非本次引入）；要彻底抽离需配合 env/user-secrets/Key Vault 提供程序覆盖。

| Section | Drives |
|---|---|
| `EnvOption` | Environment (`Env`/`ServiceName`/`MachineId`/`ServiceType`) + `InternalToken`（x-request-token 内部签名共享密钥，网关与所有服务同值） |
| `DIOption` | Type-scanning rules for Service/Repository auto-registration |
| `LogOption` | Logging backend (Serilog → Seq) |
| `CacheOption` | Redis connection + memory cache toggle |
| `DatabaseOption` | Database type, read-write split, entity scan targets, `SyncTableOnStartup`（启动时按实体同步表结构，默认关） |
| `NanaOption` | RabbitMQ host/port/credentials, consumer type list, retry count, Saga DB |
| `OutboxOption` | 发件箱：投递器开关、轮询间隔、批大小、重试上限、租约、建表、保留期。**为 null = 不启用**（见下） |
| `InboxOption` | Inbox 清理：保留期（默认 7 天）、批大小（默认 1000）、清理间隔。**与别的子配置不同 —— 为 null 不是「不启用」**：Inbox 只看 `DatabaseOption`，节点缺席就用默认值照常清理，要关掉把保留期配成 0（见下） |
| `TokenOption` | JWT secret/expiry/issuer |
| `EchoOption` | HTTP client enable + gRPC（`GrpcOption { EnableServer, Port }`） |
| `TickOption` | TickerQ scheduler config |

### DI: Autofac root + MS DI delegation

`builder.Services.AddViv(vivOptions)` registers all Banshee services into MS DI. The `AutofacServiceProviderFactory` sets Autofac as the root container — all MS DI registrations are delegated to Autofac for resolution.

Business-layer services and repositories are registered via **type scanning** driven by `DIOption` — assembly name, namespace, and class name suffix (e.g., `"ClassNameEndWith": "Service"`).

- **API:** `builder.Host.UseServiceProviderFactory(...)` + `ConfigureContainer(...)`
- **Worker:** `builder.ConfigureContainer(new AutofacServiceProviderFactory(), ...)`

`VivLocator.Initialize()` is called during startup and provides static access for scenarios where constructor injection is unavailable.

### Messaging (Nana)

基于 **Wolverine 6.25.3（MIT）** + RabbitMQ（`WolverineFx` / `WolverineFx.RabbitMQ` / `WolverineFx.EntityFrameworkCore` / `WolverineFx.RuntimeCompilation`）。对外抽象不变（`IVivEventPublisher` / `VivConsumer<T>` / `NanaEvent` / `SubscribeResult` / `NanaEnvelope<T>`），应用层无需感知传输实现。

- **Producer:** `IVivEventPublisher.PublishAsync<T>()` / `PublishDelayAsync<T>(TimeSpan, T)` — messages must extend `NanaEvent`。`NanaEventPublisher` 内部包成 `NanaEnvelope<T>`（含 `IVivContext` 快照 `Context`，并从 `LockHolderContext` 盖章 `HolderId`），调 `IMessageBus.PublishAsync` / `ScheduleAsync`。传输失败抛 `VivConnectionException(RabbitMQ)`（API 过滤器 `-503`，客户端只回固定文案「消息队列服务异常」），`false` 只表示入参无效。
- **Consumer:** Extend `VivConsumer<T>`, override `ReceiveMessageAsync()` — return `SubscribeResult` 指示成功或重投。基类 `HandleAsync(NanaEnvelope<T>, CancellationToken)`（Wolverine handler 约定，`Discovery.IncludeType` 显式注册）先按 `nana:{ServiceName}:{EventType}:{MessageId}` 取 Redis 锁（`IDistributedLock`，未注册则跳过）：**谁取到锁谁进业务**，fanout 下各服务 Key 不同所以各处理一份。锁 holder 优先 `envelope.Context.HolderId`，没有才回落 `MessageId`（**不用 TraceId**，避免客户端 `X-Trace-Id` 污染锁身份）。`DistributedLockAccessor` 把 Redis 连接失败包成 `DistributedLockException`（Inner 为 `VivConnectionException`），基类 catch 记 Warning 后抛给 Wolverine。然后将结果映射：`Success` → 确认；`Requeue` → 抛 `VivRequeueException`（走全局重试策略）；失败 → 记日志丢弃。
- **延迟重投（`VivConsumer.RedeliverAsync`）**：业务失败想延迟再试时调用 `RedeliverAsync(envelope, delay)`，把**原信封**经 RabbitMQ 延迟交换机在 delay 后重投 fanout（各订阅服务各收一份，谁爱消费谁消费，同服务消费锁保证只进一次业务）。`NanaEnvelope` 加 `ReDeliverCount`/`DelaySecond` 字段随信封透传；`IVivEventPublisher` 新增**信封版** `PublishDelayAsync(TimeSpan, NanaEnvelope<T>, ...)` 直接 `ScheduleAsync` 原信封——内容版重载会新建信封，丢 MessageId/ReDeliverCount/DelaySecond/Context（锁 Key 与计数无法存活）。重投前 `ReDeliverCount+1`，超过 `NanaOptions.RetryCount` 上限返回 Failed(IsRequeue:false) 丢弃不回队。传输失败抛 `VivConnectionException`，原消息未 ack，由 Wolverine 重试；`false` 只表示入参无效。上限取 `VivConsumerDependency._nanaOptions.RetryCount`（经 `IOptions<NanaOptions>` DI 注入，配置由 `VivConfigLoader.AddVivConfig` 注册）；`VivConsumer` 构造注入 **`VivConsumerDependency`**（聚合 `ILoggerContract`/`IVivContext`/`IVivEventPublisher`/`IOptions<NanaOptions>`/`IVivLocalEventBus`，加两个可选的 `IDistributedLock?`/`IVivUnitOfWork?` —— 前者未配 Redis、后者未配 `DatabaseOption` 时容器解析不到，Autofac 对可选参数回落默认值；`IVivLocalEventBus` **必填**因为它总是注册的。`: IDependency` 经 `AutoDependencyRegister` 自动注册 **AsSelf + Scoped**，子类构造 `: base(dependency)` 透传即可）。锁服务异常（`DistributedLockException`，Redis 故障时 Inner 为 `VivConnectionException`）记 Warning 后抛给 Wolverine 重试/死信。
- **配置（`AddVivWolverine`，`AddViv()` 内调用）：**
  - `UseRabbitMq(amqp://user:pass@host:port/vhost).AutoProvision()` — 队列/交换机自动声明（**先清理旧 MassTransit 拓扑遗留的队列**，否则 `406 PRECONDITION_FAILED`）。
  - **发布订阅拓扑**：发布侧 `PublishMessage<NanaEnvelope<T>>().ToRabbitExchange({EventName}Exchange)`（fanout 交换机）；消费侧 `ListenToRabbitQueue({EventName}Queue.{ServiceName})` + `transport.BindExchange({EventName}Exchange, ExchangeType.Fanout).ToQueue(queue)`——每个订阅服务建一条**独立队列**绑到交换机，**各收一份**（`NanaRegister.GetExchangeName`/`GetConsumerQueueName` 约定；`ServiceName` = 入口程序集名，同服务多实例共享队列轮询）。**同服务只执行一次由 `VivConsumer.HandleAsync` 取 Redis 锁保证**（Key = `nana:{ServiceName}:{EventType}:{MessageId}`，拿到进业务、拿不到丢弃）；框架仍只负责广播，锁按服务隔离所以 Apex 与 DeepRed 会各处理一份。
  - **消费并发/预取调优**：`VivConsumer<T>` 子类可标 `[NanaConsumer(ConsumerCount, PrefetchCount, MaximumParallelMessages)]` 控制该队列的消费通道数（>1 丢失同队列严格顺序）、每通道预取（basic.qos）、端点最大并行；特性缺席回落框架默认 **prefetch=20**（收敛 Wolverine 原生 100，降低崩溃重投放大）、队列 **Quorum**（多副本防丢消息）。`AddVivWolverine` 内**直接写 `RabbitMqQueue` 属性**（`VivWolverineConfigurationExtensions`）——该 fork 的 fluent `PreFetchCount/ListenerCount/QueueType` 是空壳（编译通过但不落盘），必须直写。⚠️ 已存在的 classic 队列不会自动变 quorum，重声明类型不一致会 `406 PRECONDITION_FAILED`，切换前需清掉旧队列。
  - 全局失败策略：`OnException<Exception>().RetryWithCooldown(指数退避 5s 起、最大 60s，共 RetryCount 次).Then.MoveToErrorQueue()`（死信 → `wolverine-dead-letter-queue`）。
  - **EF Saga 持久化**：`NanaOption.SagaConnectionString` 已配且扫到 `VivSagaState` 子类（`TypeScanMagic.ScanTypes<VivSagaState>()`，需 `ForceLoadReferencedAssemblies()` 强制加载业务 Core 程序集）时启用：`opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Lightweight)`（**内联在 options 里**，规避 JasperFx/wolverine#1140 DI 修改 bug；**Lightweight = 无 durable outbox**，默认 Eager 要求数据库消息持久化会抛 "not using Database backed message persistence"）+ `VivSagaDbContext` 映射 `Saga_{SagaTypeName}` 表。
  - **Saga 实体主键**：`VivSagaDbContext.OnModelCreating` 用 `[SagaIdentity]` 标记的属性（如 `OrderSaga.OrderId`）显式 `HasKey`——EF 无法从 Saga 类型推断主键（`Id`/`Version` 都不是约定名），不配置会抛 "requires a primary key"，Wolverine 进而判定无 EF 持久化提供者（"No known Saga persistence provider"）。saga 表（`Saga_OrderSaga`）需预先建好（`EnsureCreated`/迁移）。
  - `TypeLoadMode.Dynamic`（开发默认）需引用 `WolverineFx.RuntimeCompilation`。

#### 本地队列（第三种事件通道）

四族事件的总览（另两族见本节的跨进程那条线、`### Outbox（发件箱）` 与 `### Local events (IVivLocalEventBus)`）。本地队列补的就是「**不阻塞调用方 + 不出网 + 自带重试/延迟**」这个中间档（其余三族都给不了）：

| 基类 | 投递 | 发布接口 | 消费端写法 | 语义 |
|---|---|---|---|---|
| `NanaEvent` | RabbitMQ | `IVivEventPublisher` | `VivConsumer<T>` | 跨进程、fanout、每服务各收一份、**当场发** |
| `NanaEvent` | RabbitMQ | **`IVivOutbox`** | `VivConsumer<T>` | 跨进程、fanout、**与业务写同事务**（可靠版，见 `### Outbox（发件箱）`） |
| `NanaLocalEvent` | Wolverine 本地队列 | `IVivLocalEventPublisher` | `VivLocalConsumer<T>` | 进程内、**点对点**、异步、**独立 DI 作用域** |
| `LocalEvent` | 本地总线 | `IVivLocalEventBus` | `LocalEventHandler<T>` | 进程内、fanout、同步、**同 DI 作用域** |

**没有第五个基类** —— Outbox 复用 `NanaEvent`，只是换了投递时机（入队 ≠ 发送）。

**与跨进程那条线完全解耦（自带一整套类型，一个现有文件都不碰）**：`IVivLocalEventPublisher` 是独立接口（不是往 `IVivEventPublisher` 加方法 —— 那会破坏它的全部实现者）；`NanaLocalEventPublisher` / `VivLocalConsumer<T>` / `VivLocalConsumerDependency` 同理。本地这条线不引用 `IVivEventPublisher` / `VivConsumer` / `IDistributedLock`。要「一个事件触发多个反应」用 `LocalEvent` + 本地总线（fanout），本地队列是**一事件一消费者**。

- **⚠️ `NanaLocalEvent` 是 `NanaEvent` 的平行根，绝不是子类**：`VivWolverineConfigurationExtensions` 对**每个 `NanaEvent` 子类**都注册 `PublishMessage(env).ToRabbitExchange(...)`，一旦继承，所有本地事件被绑死成跨进程语义、`PublishAsync` **双发**（MQ + 本地队列），编译期毫无提示。名字带 Nana 极易顺手写 `: NanaEvent`，故有防回归测试钉死（`NanaLocalEventTests.NanaLocalEvent是空标记基类_且与NanaEvent互不继承`）。同理 `NanaEnvelope<T>` 约束写死 `where T : NanaEvent`，改不得，本地队列用自己的 `NanaLocalEnvelope<T>`。
- **拓扑与注册**：`AddVivWolverine` 内扫 `ScanTypes<NanaLocalEvent>()` 逐条 `opts.LocalQueue({EventName}LocalQueue)` + `PublishMessage(NanaLocalEnvelope<T>).ToLocalQueue(queue)`（`NanaRegister.GetLocalQueueName`，与 `GetQueueName`/`GetExchangeName` 共用 `StripEventSuffix`）。**按全部子类扫描而非按消费者反推** —— 与跨进程那段对称，保证「发布必有路由」，否则无消费者的本地事件会落进 Wolverine 约定路由。**消费端循环零改动**：`Discovery.IncludeType` 在 `ExtractMessageType` 之前调用，`VivLocalConsumer<T>` 子类返回 `null` 走 `continue`，Wolverine 已能发现其 `HandleAsync`（与现有消费者同一目录即可）。`AddViv()` 里 `AddScoped<IVivLocalEventPublisher, NanaLocalEventPublisher>()`。**`NanaOptions` 不加任何新配置项** —— 没有本地事件时循环空转，零成本。
- **孤儿告警**：无消费者的本地事件没有 RabbitMQ「无绑定队列即丢弃」那种兜底，就是真堆内存。注册期拿不到 logger（`VivLocator` 未初始化），故扫描结论存 `NanaRegister` 静态（`LocalEventTypeCount` / `OrphanLocalEvents` / `RecordLocalQueueScan`），由 `NanaLocalEventPublisher` 首次构造时打一次启动日志 + 逐个 Warning（照 `LocalEventBus`「未分发」Warning 的先例）。
- **`HandleAsync` 与 `VivConsumer` 的差异只有两处**：**无 Redis 消费锁段**（进程内点对点、无 fanout，不存在多实例抢锁），故也**无 `catch (DistributedLockException)`**。其余一致：从信封水合 `IVivContext`、盖 `LockHolderContext`（信封 `HolderId` 优先，无则回落 `MessageId`，**不用 TraceId**）、`Success` 确认 / `Requeue` 抛 `VivRequeueException` / 失败记日志丢弃、`finally` 清理上下文。
- **信封必须带 `Context`**：handler 跑在**后台线程 + 独立 DI 作用域**，`AsyncLocal` 租户上下文不会跟过去；而 `EFAppContext` 全局过滤器在「无上下文」时**不过滤** = 跨租户读。靠 `NanaLocalEnvelope<T>.Context`（发布时快照 + 盖 holder）把租户带过去水合。
- **延迟消息重启即丢**：`PublishDelayAsync` 走 `InMemoryScheduledJobProcessor`（`NullMessageStore` 路径，**不抛异常**），进程重启后未到期消息消失 —— 与 `IVivEventPublisher.PublishDelayAsync` 当前行为**完全一致**，非本次引入。要持久化需另开 `PersistMessagesWithSqlServer` + 迁移。**不要当「延迟队列」用**。
- **失败重试**：全局策略（`OnException<Exception>().RetryWithCooldown(...).Then.MoveToErrorQueue()`）对本地队列同样生效。发布器**不包 `VivConnectionException`** —— 本地队列纯内存、无网络传输，包成 RabbitMQ 连接异常是撒谎，异常原样冒泡。
- **不做（v1 范围外）**：`VivLocalConsumer.RedeliverAsync` 延迟重投（瞬时失败由全局退避重试兜底）；延迟消息跨重启持久化。

### Local events (IVivLocalEventBus)

进程内**解耦的同步调用**，是四族事件里最"没架子"的一族（其余三族见 `### Messaging (Nana)` 与 `### Outbox（发件箱）`）。这条线**自己不 import 任何 `Viv.Nana` 命名空间**。反向的依赖是有的：`VivConsumer` / `VivLocalConsumer` 的 `HandleAsync` 注入 `IVivLocalEventBus` 当作消息消费侧的分发触发点（见 `### Messaging (Nana)` 的「触发点 ③」）——「Nana 的消费者基类认识一个 `Viv.Contracts` 里的接口」，层级方向正常；但四族事件本身仍互不继承、互不引用。

- **用途对比**：跨进程走 `IVivEventPublisher`（出网、消费端是另一个进程/另一个 DI 作用域）；本地事件不出网，**handler 与发布方同一 DI 作用域** —— 注入的 `IMomoDbContext` / `IVivContext` 就是发布方那一个。入队可以发生在工作单元之内；**Flush 发生在成功提交之后**，handler 失败**不能**回滚已经落库的写。需要与写库原子的跨进程消息请用发件箱。`IVivLocalEventPublisher`（Nana 本地队列）同样**不加入**调用方的数据库事务。
- **分发时机**：`PublishAsync` **只入队**，真正分发推迟到**这次请求 / 这次消息消费**正常结束时（触发点见下）。保证 handler 看到「最终定格」的数据状态；失败 → 整队丢弃，一条事件都不发（不留幽灵事件）。
- **事件类型约束（硬约束）**：事件**必须继承 `LocalEvent`** —— `Viv.Contracts` 里的空标记抽象基类，纯限制：本地事件的 handler 写下来就必须执行，所以事件类型不允许随手写（`PublishAsync(new object())` 编译不过）。**各走各的**：要跨进程继承 `NanaEvent` 走 `IVivEventPublisher`（要原子就换成 `IVivOutbox`，事件类型不变），进程内异步点对点继承 `NanaLocalEvent` 走 `IVivLocalEventPublisher`，进程内同步 fanout 继承 `LocalEvent` 走 `IVivLocalEventBus`。⚠️ **绝不可让 `LocalEvent` 去继承 `NanaEvent`** —— `VivWolverineConfigurationExtensions` 对每个 `NanaEvent` 子类都注册了 `ToRabbitExchange` 路由，继承即把所有本地事件绑死成跨进程语义（有防回归测试守着这两点：空标记 + 与 NanaEvent 无继承关系）。
- **写法（两种，都无需特性 / 无需 IDependency）**：继承 `LocalEventHandler<TEvent>`（与 `VivConsumer<T>` 同手感；C# 单继承，一个类只能订阅一个事件）**或**直接实现 `IVivLocalEventHandler<TEvent>`（可订阅多个事件）。扫描目标统一是接口，注册逻辑只有一份。
- **注册**：`AddViv()` → `VivRegister.Register` → `LocalEventRegistration.Register` —— `ForceLoadReferencedAssemblies()` + `ScanTypes(typeof(IVivLocalEventHandler<>))`（`TypeScanMagic.IsMatchType` 已支持开放泛型匹配），遍历处理器的**全部**闭合接口注册（订阅多事件时不漏），再按事件类型注册闭合分发器 `LocalEventHandlerInvoker<T>`，最后 `AddScoped<IVivLocalEventBus, LocalEventBus>()` + `AddScoped<IVivLocalEventScope, LocalEventScope>()`。**泛型处理器定义会被跳过**（闭合 TEvent 未知，继续注册会抛）。这里**扫到 0 个处理器是合法的**（与业务 Service 注册不同），只记日志不报错。
- **`LocalEventBus` 是 Scoped**（与 `IMomoDbContext` / `IVivContext` 同作用域，这是整个设计的支点）。三态状态机 `Pending` → `Draining` → `Done`：`Draining` 态**允许入队**（处理器内递归发布合法，进下一轮）；`FlushAsync` 最多 5 轮，超限记 Error「疑似递归发布」；`Discard`/`Flush` 均幂等。处理器抛异常**直接上抛**（本地事件是主业务流的一部分，不静默吞）。
- **分发器为什么绕一层**：Autofac 作根容器时注入的 `IServiceProvider` 很可能解析到**根作用域**，会把 Scoped 处理器连其 Scoped 依赖解析到根上 —— 正好摧毁「同作用域」这一支点。改为**启动期注册闭合泛型、运行期构造注入** `IEnumerable<IVivLocalEventHandler<TEvent>>`，由 Autofac 从当前作用域解析；flush 时零反射、零容器查询。
- **handler 必须跑完**：HTTP / 消费者 / `IVivLocalEventScope` 触发点一律传 `CancellationToken.None`，**刻意不用 `HttpContext.RequestAborted`** —— 客户端中途断开不该造成「主业务已提交、通知没发出去」的脱节。真能容忍不执行的逻辑就不该用本地事件，该走 MQ。
- **触发点**：① HTTP 主路径 `LocalEventFlushFilterAttribute`（`AddVivApi` 的 `AddMvc` 全局过滤器，排在 `VivExceptionFilterAttribute` **之后**）—— 用 action filter 而不是中间件，因为位置在结果执行/响应写出**之前**，且**看得见业务成败**：异常过滤器置 `ExceptionHandled=true` 后 MVC 会剥离 `ActionExecutedContext.Exception`，唯一的失败信号只剩 `context.Result` 里那个错误 `VivApiResult`（判定用 **2xx 区间**，`Accepted=201` 也算成功）。`next()` 抛异常时过滤器 catch 后 Discard 再上抛，避免「异常过滤器把 HTTP 写成 200、中间件按状态码 Flush」。Flush 失败（主写入若已提交）会把信封改成错误并记 Error「主业务已提交，本地事件分发失败」。② 兜底 `LocalEventFlushMiddleware`（覆盖非 MVC 端点 gRPC / SignalR / health），**必须挂在 `VivContextMiddleware` 之内**（分发要跑在它 `finally Clear()` 租户上下文之前，否则处理器拿不到 `IVivContext`、租户过滤失效）。除 HTTP ≥400 外，还读 `HttpContext.Items[VivRunDefine.ApiResultItemKey]` 里写出的信封，避免「HTTP 200 + 信封非 2xx」误 Flush。响应已开始则无法改写信封，只记日志并丢弃剩余事件。
- **触发点 ③ 消息消费**：`VivConsumer<T>` / `VivLocalConsumer<T>` 的 `HandleAsync` 在 `finally` 里分发 —— 消费成功才 `FlushAsync(CancellationToken.None)`，其余路径（抢锁失败 / `Requeue` / 失败丢弃 / 抛异常）一律 `Discard()`。**依赖是「Nana 的消费者基类认识一个 `Viv.Contracts` 里的接口」**，层级方向正常、不产生新项目引用（`Viv.Nana` 本来就引用 `Viv.Contracts`）—— 与当初把 `VivUnitOfWorkAttribute` 放进 Contracts 是同一个先例。「不耦合 Nana」那条约束针对的是**四族事件互相继承**，不是这个。两个细节：① **必须在 `_context?.Clear()` 之前**（同 HTTP 侧中间件必须挂在 `VivContextMiddleware` 之内那个坑）；② **失败走 `Discard` 而非 `Flush`** —— `Discard` 不抛异常，而 `FlushAsync` 会，`finally` 里抛出的异常会**顶掉在途的 `VivRequeueException`**，把重投语义换成不相干的异常。依赖经 `VivConsumerDependency._localEventBus` / `VivLocalConsumerDependency._localEventBus` 注入，**必填不给默认值**（它总是注册的，可空只会制造「忘了传就静默不分发」的坑）。构造点全在测试里（`Viv.Nana.Tests` 的 6 个 helper + `Viv.Herta.Tests` 一处），生产侧一律 DI 注入，业务消费者只管 `: base(dependency)` 透传，零改动。
- **触发点 ④ TickerQ 定时任务**：继承 `VivTickerJobBase`（`Viv.Clockwork`），任务体走它保护的 `RunAsync` 即可 —— 内部经 `VivTickerJob.ExecuteAsync` 调 `IVivLocalEventScope.RunAsync`（成功 Flush、失败 Discard 后上抛），并在开工前**硬校验租户快照**（`EnsureSnapshot`：没 `SetSnapshot` 或 `AppId <= 0` 直接抛，不让全 0 快照静默跑 —— 那会让 EF 租户过滤失效、跨租户读）。SakuMai 的 `BaseJob` 就是这么写的，两个 `[TickerFunction]` 入口都经过 `RunAsync`。**手写 `BackgroundService` 没有统一入口可挂**（`Apex.Worker/Worker.cs`、`DeepRed.Worker/Worker.cs` 是 `dotnet new worker` 模板残留，一秒打一行日志、不碰库），真要发本地事件得自己包 `IVivLocalEventScope.RunAsync` 并放在清理 `IVivContext` 之前；不包的话作用域结束只记一条「未分发」Warning，事件不会被分发。
- **与 UoW 的衔接（⚠️ 单向，别想反）**：目前 `MomoDatabaseContext` 每次写当场提交，「主业务失败 → 事件不发」**现在就有**（失败则整队丢弃）。反过来**做不到**：分发触发点（action filter / middleware / 消费者 `HandleAsync` 的 `finally`）在**服务方法的提交边界之外**，等 handler 跑到时事务早就提交了 —— 此时 handler 失败**无法回滚已提交的写**。所以 handler 里只能做「失败就记日志 / 走补偿」的动作，别指望它还原子；真需要原子就得让 handler 自己开窄事务，或者把这段逻辑收回主业务流。⚠️ **上一版这里写着「届时只需把 flush 触发点上移到事务提交后」—— 那句是错的**，提交后 flush 拿不回已经落库的写。

### Unit of Work（事务）

**两种模式，业务自行取舍** —— 框架不替业务选：

| 模式 | 入口 | 边界 | 适合 |
|---|---|---|---|
| **窄事务** | `IVivTransaction`，`await using` 自动释放 | 业务自己划线，写多少包多少 | 只包住几行写；或事务里要夹非数据库动作（发消息、算东西） |
| **完整事务** | 特性 `[VivUnitOfWork]` + Castle 接口代理 | **整个方法**：进方法开、出方法提交 | 一个应用服务方法就是一次业务操作，不想在业务代码里看见事务 |

```csharp
// 窄事务
await using var tx = await _unitOfWork.BeginAsync();
await _orderRepo.InsertAsync(order);
await _itemRepo.InsertBatchAsync(items);
await tx.CommitAsync();          // 不写这行 → 离开作用域自动回滚

// 完整事务（public virtual 是硬要求，见下）
[VivUnitOfWork]
public virtual async Task<VivApiResult> CreateOrderAsync(...) { ... }
```

契约（`VivUnitOfWorkAttribute` / `IVivTransaction` / `IVivUnitOfWork`）在 **`Viv.Contracts`** —— Worker 侧要读同一个特性，放这儿两边都不用新建引用。实现全在 **`Viv.Engine/UnitOfWork/`**（拦截器要判 `VivApiResult` 信封，而 `VivApiResult` 就在 `Viv.Engine`）——**唯一例外是 `FailDetector.cs`，它住在 `Viv.Engine/` 根**（见下）。

- **⚠️ 特性一律标在实现类上，绝不标接口** —— 接口上标了**完全不生效**：类型级根本标不上去（`AttributeTargets.Class` 不覆盖 interface，实测 `CS0592`），方法级能编译但没人读（注册期扫的是**实现类型**，运行期读 `MethodInvocationTarget`；而接口成员的特性不会被实现方法继承，`inherit: true` 只沿基类链走）。表现是「能编译、能跑、就是不原子」。**本版刻意不支持接口标注**：即便支持，实现方法照样得 `public virtual`（隐式实现接口的 public 方法是 `virtual final`，Castle 重写不了），业务省不下任何东西，反而把「凡实现该接口的类都被开事务」这个副作用藏进契约里。
- **嵌套语义（没有保存点，不是 `TransactionScope`）**：只有**最外层**那次 `BeginAsync` 真正开事务，嵌套返回**子句柄**、不穿透到数据库。子句柄 `CommitAsync()` 是**空操作**（等最外层）；子句柄**未提交就释放 / 显式 `RollbackAsync` / 抛异常** → 整个作用域打上 **rollback-only（粘性）**，此后最外层再调 `CommitAsync` 会先回滚再抛 `VivUnitOfWorkException`（不只记 Warning）—— 拦截器必须看见失败，本地事件才能 Discard 而不是 Flush。**没有保存点** —— 内层回滚不会「只撤销内层的写」，它会拖垮整个事务；要部分回滚就自己用窄事务划线。`CommitAsync`/`RollbackAsync` 均幂等。**被拦方法上的 `CancellationToken` 只传给 `BeginAsync`；`Commit`/`Rollback` 一律 `CancellationToken.None`** —— MVC action 那个令牌是 `HttpContext.RequestAborted`，客户端断线就会取消：提交被取消的后果是「业务成功返回、写却全丢」（`MomoDatabase.CommitTransactionAsync` 对 `OperationCanceledException` 是原样上抛 + `finally` 里 Dispose），回滚被取消则留下没关掉的事务。与本地事件分发那几处传 None 同一取舍。消费者侧 `ConsumerUnitOfWork` 同理。
- **异步拦截必须用第三方包**：`Castle.Core.AsyncInterceptor` 的 `AsyncInterceptorBase`。裸 `IInterceptor.Proceed()` **在第一个 `await` 处就返回**，提交会早于业务方法真正结束 —— 事务边界直接错位。`AsyncInterceptorBase` 实现的是 `IAsyncInterceptor`（**不是** `IInterceptor`），所以 `InterceptedBy` 只认 `IInterceptor`，中间必须垫一层 `AsyncDeterminationInterceptor` 适配器（`Autofac.Extras.DynamicProxy` + `Castle.DynamicProxy`）。有测试钉死这点：提交那一刻的回调里断言业务方法体**已经跑完**。
- **注册期硬校验（把静默失效变成启动失败）**：接口代理失效时**完全无声** —— 没代理上就没有事务，业务照跑、数据照写、只是不原子，往往到线上数据对不上才发现。所以凡是能静态判定的原因一律 `throw`，启动就挂：非 `public` / `static` / 泛型方法 / 同步方法 / 返回**非泛型** `ValueTask` / 不可重写 / 类型没有任何接口 / 类型**没按接口注册**（漏进 `DIOption` 扫描、或标了 `[VivDependency(AsSelf = true)]`）/ 标了特性却没配 `DatabaseOption` / **开放泛型类型上标了特性**（`RegisterGeneric` 不挂代理）。类级与方法级同一把尺子。
  - ★ **不可重写的判据是 `IsVirtual && !IsFinal`，不能只判 `!IsVirtual`**：C# 会把「**隐式实现接口的 public 方法**」编译成 **`virtual final`** —— `IsVirtual` 为 true 但 sealed，Castle 重写不了。只判 `!IsVirtual` 恰好把最容易写出的那种方法放过去，失效还无声。有回归测试钉死这个元数据事实（`UnitOfWorkRegistrationTests`）。
  - 类级特性与方法级同一把尺子：不可重写（含隐式接口实现的 virtual+final）或同步方法一律启动失败；开放泛型类型上标了特性同样启动失败（`RegisterGeneric` 不挂接口代理）。想让个别方法豁免，标 `[VivUnitOfWork(Enabled = false)]`。
  - **只有带特性的类型才挂代理**，不开全局拦截 —— 没标的零代理开销、零调试干扰。拦截器与适配器都注册成 `InstancePerLifetimeScope`（已实测 Autofac 的接口代理从**当前**作用域解析拦截器，不会落到根作用域 —— 落根作用域会让并发请求共用同一个事务状态机）。
  - **只剩自调用拦不住**：`this.OtherMethod()` 走真实实例、不过代理，标了也不生效。判它要分析 IL 调用点，本版不做。**特性标在最外层公开方法上。**
- **成败判定与本地事件同源（同一份实现，不是两份抄得像）**：`FailDetector.IsFailed` 判 `VivApiResult` 的 **2xx 区间**（`Accepted=201` 也算成功）。`LocalEventFlushFilterAttribute.IsFailed` 对信封那一段**直接调它**——以前是抄了一份表达式、靠一个反射测试盯着两份不漂移，现在只有一份，没有可漂移的东西。**`FailDetector` 因此住在 `Viv.Engine/` 根、与它判定的对象 `VivApiResult` 并排**，而不是塞进 `UnitOfWork/`（塞进去就变成「事务的实现细节被过滤器依赖」）。判据共三条：`VivApiResult` 看 2xx；**`IBooleanResult`（`Viv.Delusion`，`FuncResult` 已实现）取反 `IsSuccess`** —— 业务方法直接返回 `FuncResult` 时成败也认；其余返回值（含未拆包的 `Task<T>`，不能 `.Result` 阻塞）一律视为成功。异常一律回滚后**原样上抛**，不吞。
- **与本地事件的顺序天然正确，不需要钩子**：拦截器在**服务方法**边界提交，flush 触发点是 **MVC action filter**，位置在外层 ⇒ 必然 `提交 → action 返回 → flush`。绝不能反（先 flush 再提交 = handler 看见脏数据 + 回滚后留下幽灵事件）。
- **Momo 事务内核已修（原先是坏的，且从没人用过）**：`MomoDatabase.BeginTransaction` / `BeginTransactionAsync` 原先写 `(IDbTransaction)context.Database.BeginTransaction()`，而 EF 的 `IDbContextTransaction`（`SqlServerTransaction`）**不是** `IDbTransaction` —— 强转必抛 `InvalidCastException`，**而那时真事务已经开在连接上了**，句柄没存住就成了提不了也滚不掉的**悬挂事务**（`IsInTransaction` 为 false、回滚被 `_transaction == null` 挡成 no-op）。同形状的强转在 `ExecuteSqlList` / `ExecuteSqlListAsync` 里还有 **4 处**（自建事务那条路径；外层已有事务时被 `??` 短路所以一直没暴露）。现统一走 **`MomoDatabase.GetDbTransaction(IDbContextTransaction)`**（`IInfrastructure<DbTransaction>.Instance`）——⚠️ **EF Core 10 没有现成的 `GetDbTransaction()` 扩展方法，别去找**（试过，编译不过，这个 helper 就是为此而写）。字段类型仍是 `IDbTransaction?`（Dapper 要它），判空 / `?.Dispose()` / `= null` 逻辑一字未改。实测：开/提/滚、`IsInTransaction` 全部名副其实，悬挂事务消失。
- **⚠️ 事务内的 Dapper 读会直接抛异常 —— 框架不管，业务自己规避**：12 处原生 SQL 逃生口（`FindScalar` / `FindList<T>(sql)` / `Page` 等，`MomoDatabaseContext.cs:1055,1098,1125,1146,1179,1212,1278,1295,1311,1334,1363,1367`）把 `null` 硬编码成 Dapper 的事务参数，实测抛 `VivConnectionException`：*「如果分配给命令的连接位于本地挂起事务中，ExecuteReader 要求命令拥有事务。命令的 Transaction 属性尚未初始化。」*—— **是硬失败，不是脏读**（当前所有服务 `IsReadWriteSplit: false`，`CreateEFAppContext` 把读强转成 Write，读写**共用同一条连接**；将来真开读写分离才会退化成脏读）。走 EF 的 `Find<T>` / `Exist` / `Count` / 谓词版 `FindList` **不受影响**。**框架立场：事务只针对主库写，读不开事务 —— 业务先把数据备好，再开事务。这是业务的活，不是框架的活。**（`ExecuteSqlList` 那条路是例外，它自己把 `_transaction` 传给 Dapper，实测事务内可正常用。）
- **Worker / 消费者**：`VivConsumer<T>` / `VivLocalConsumer<T>.HandleAsync` 按类级或 `ReceiveMessageAsync` 上的 `[VivUnitOfWork]` 显式开合事务（不走接口代理，消费者子类在注册期豁免代理校验）。成功提交后才 Flush；失败 / 重投 / 抛异常回滚并 Discard。标了特性却拿不到 `IVivUnitOfWork`（没配 `DatabaseOption`）时构造即失败，不会静默裸奔。
- **不做（范围外）**：DataFilter / 权限。（**审计接口已另立一节**，见 `### Entity audit` —— 它由 Momo 数据层做，与事务无关。）

### Outbox（发件箱）

**解决的是「写库 + 发消息」不原子**：`await _repo.InsertAsync(order); await _publisher.PublishAsync(new OrderCreatedEvent{...});` —— 先写后发则发失败就永久丢消息、先发后写则消息出去了业务回滚，下游拿着不存在的订单干活。**四族事件通道里只有这一族管原子性**，其余三族都只管投递（`NanaEventPublisher.PublishAsync` 是「当场发出去」，与数据库事务没有任何关系）。

**Outbox 是跨进程那族的可靠版本**：事件类型不变（必须继承 `NanaEvent`，复用现成的 `{EventName}Exchange` fanout 拓扑与消费端 `VivConsumer<T>`），**只是换了投递时机** —— 入队 ≠ 发送。原子性是这个模式唯一的产出。

```csharp
// 窄事务 + 发件箱：订单与待发消息一起成立
await using var tx = await _unitOfWork.BeginAsync();
await _orderRepo.InsertAsync(order);
await _outbox.EnqueueAsync(new OrderCreatedEvent { OrderId = order.Id });
await tx.CommitAsync();

// 或完整事务（[VivUnitOfWork] 挂在外层公开方法上）
[VivUnitOfWork]
public virtual async Task<VivApiResult> CreateOrderAsync(...) { ...; await _outbox.EnqueueAsync(...); }
```

- **⚠️ `src/Banshee/Viv.Momo/**` 一行未动 —— 读写分离是 Momo 最大的价值。** 本模块**完全不碰 EF**：表由手写 SQL 经 `IMomoDbContext` 现成的 Dapper 逃生口读写，`OutboxMessage` 是**普通 POCO，不实现 `IEntity` / 不实现 `ITenant`**、不注册 `EntityTypeOptions`。理由三条：① EF 一接管，表名/列名就由命名约定生成（`outbox_message` vs `OutboxMessage`），而手写 SQL 用的是不带引号的 PascalCase，两套命名对不上是运行期才炸；② `ITenant` 会带来全局查询过滤器，而投递器跑在后台作用域里，「无上下文/无租户」时过滤规则会静默把行读成空；③ 绕开命名机制本身 —— **DDL 与所有 SQL 全部不带引号**，SqlServer 不区分、PG 统一折叠成小写，两端天然一致。有防回归测试钉死（`OutboxContractTests.OutboxMessage既不实现IEntity也不实现ITenant`）。
- **原子性的唯一机关是 `ExecuteSqlAsync`**：它用**写库**上下文（`CreateEFAppContext(Write)`）并把 `_transaction` 转发给 Dapper（`MomoDatabaseContext.cs:688`），所以入队自动并入调用方当前的事务。**换成任何走读连接的执行方式都会让它静默失效**（有持久性、没有原子性，测试全绿、只是不原子）。`OutboxStore` 因此必须 **Scoped** —— 与业务拿到的 `IMomoDbContext` 同作用域才共享同一个 `_transaction` 字段；改成 Singleton / Transient 就等于把入队挪到事务外面去（`OutboxRegisterTests.入队器与仓储都是Scoped` 钉死）。
- **认领（`ClaimBatch`）是唯一的例外，它不走 `ExecuteSqlAsync` 也不走 `FindListAsync`**：所有返回行的原生 SQL 方法（`FindListAsync<T>(sql)` 等）走的都是**读库**上下文，开启读写分离后会打到从库上。改为 `_db.GetDbConnection(DbReadWriteType.Write)` + 自己跑 Dapper（`transaction: null` —— 认领是单条自原子语句，不需要事务参数）。这条路**恰好也绕开了「事务内 Dapper 读必抛」那个坑**：投递器自带 `IServiceScope`，作用域里没有环境事务。
- **多实例安全靠原子认领，不靠 Redis 锁**：SqlServer `UPDATE VivOutboxMessage WITH (READPAST) ... OUTPUT inserted.*`，PG `UPDATE ... WHERE Id IN (SELECT ... LIMIT @BatchSize FOR UPDATE SKIP LOCKED) ... RETURNING *`。**必须是单条 `UPDATE`** —— 先 `SELECT` 再 `UPDATE` 会留一个窗口，同一条消息被两个实例各投一遍（而且编译通过、测试也通过）。这比 `VivConsumer` 那把 Redis 消费锁更强的地方在于：不需要 Redis、不区分谁持锁。
- **表 `VivOutboxMessage`**（业务主库，前缀 `Viv` 让它在业务库里一眼可辨是框架表；`Sql/OutboxMessage.sql` + `Sql/OutboxMessage.pg.sql` 作嵌入资源 —— **文件名与表名不绑定**，同一份文件也提交进仓库供 DBA / 迁移脚本用）：`Id`（`IdMagic.NextId()`，**不用 IDENTITY**，手写 INSERT 不依赖回填）、`MessageId`、`EventType`、`Payload`、`Status`、`RetryCount`、`NextRetryAt`、`LeaseUntil`、`OccurredAt`、`SentAt`、`LastError`。`Status`：`0=Pending`、`1=Processing`（已认领、有租约）、`2=Sent`、`3=Failed`（耗尽重试，等人工介入）—— **数值即库里的值**，改一次就等于把所有历史行解读错（有测试钉死）。所有时间列一律 `DateTime.UtcNow` 派生（Kind=Utc）：PG 侧是 `TIMESTAMPTZ`，Npgsql 拒绝写入 Kind=Unspecified。
- **`EventType` 存 `FullName` 而不是程序集限定名**：AQN 里带着程序集版本号，一次发版就会让库里旧行的类型解析不出来。解析走进程内静态索引（`TypeScanMagic.ScanTypes<NanaEvent>()`，先 `ForceLoadReferencedAssemblies()` —— 业务 Core 常是懒加载，不加载扫出来的事件类型是残缺的，表现成「库里的消息投递不出去」，很难查），`Type.GetType` 兜底。**解析不到 → 置 `Status=3` + Error 日志，绝不静默丢**（多半是 `EventType` 写错或程序集没加载）。
- **`Payload` 是 `NanaEnvelope<T>` 的 JSON，自产自销**：由 Outbox 自己 `Serialize`、自己 `Deserialize`，**Wolverine 从头到尾看不到这个 Json**（重投时 Wolverine 才用**它自己的**序列化器把重建出的信封发上线）。因此完全不需要知道 Wolverine 的 `JsonSerializerOptions`，也就没有「选项漂移导致静默反序列化成默认值」的风险。选项固定成 `new JsonSerializerOptions(JsonSerializerDefaults.Web)`（camelCase + 大小写不敏感），由 `OutboxContractTests` 的 round-trip 钉住。**投递时 `MessageId` 从数据库列回填、覆盖 payload 里的值** —— 它是消费端 `nana:{ServiceName}:{EventType}:{MessageId}` 那把消费锁的去重键，必须活过重投。
- **投递走 `IVivEventPublisher.PublishEnvelopeAsync`（新增的信封版，刻意不叫 `PublishAsync` 重载）**：「原样重发」保留 MessageId / Context / ReDeliverCount / CreatedAt，且**不重新盖 holderId**（投递的是冻结的信封）。⚠️ 不能走内容版：内容版每次新建信封、`MessageId` 重新生成，消费端去重键就没了。**为什么不叫 `PublishAsync`**：与内容版同为一个参数时，调用点写 `PublishAsync<T>(null)` 的 `null` 字面量对两个重载都成立且互不更优 → `CS0121` 二义（实测踩过），换个名字彻底躲开。
- **投递器 `OutboxDispatcher : BackgroundService` 每轮**：① 释放过期租约（`Status=1 AND LeaseUntil <= @Now` → 退回 Pending，崩溃/被杀后卡住的行靠这条复活）② **排空**认领 + 投递（一直认领到认不出为止；只看一批的话积压时投递速度会被轮询间隔卡死）③ 分批清理 `Status=2 AND SentAt < @Cutoff` ④ 睡 `PollIntervalSeconds`（默认 5s，**纯轮询、无「提交后立即试投」的快路径** —— 投递只有一条代码路径）。
- **`AddHostedService<OutboxDispatcher>()` —— 这是本模块对既有约定唯一的刻意破坏**：框架至今从不自己注册 `IHostedService`。但只有 Apex / DeepRed 有 Worker 进程，而 Herta.Api / SakuMai.Api 也会写 outbox —— 投递器只在 Worker 跑，这些服务的消息就永远发不出去；让每个宿主手工 `AddHostedService` 又违背「一行启动」的姿态。故由配置门控自动注册，凡调 `AddViv` 的宿主（API + Worker）都会跑投递器，与 `AddVivGrpcKestrel` 的配置驱动装配同一姿态。**它是 Singleton，所以不能构造注入任何 Scoped 服务**（`IVivEventPublisher` / `IOutboxRepository` 都是 Scoped），每轮由 `OutboxWorker` 自己 `CreateScope` 去解析。
- **后台异常一律吞掉**：`BackgroundService` 里逃出去的异常在 .NET 6+ 会**直接停掉整个宿主** —— MQ 抖一下整个业务进程跟着死。`OutboxDispatcher` catch 后记 Error 继续下一轮。**真正干活的一轮逻辑在 `OutboxWorker`（可测：`RunOnceAsync` 返回本轮投递条数），它负责报错、不负责吞**（吞是调度层的职责）。
- **失败处理**：投递失败一律先重试，**不区分「MQ 挂了」和「序列化/路由问题」**（两类在这里的处理本来就是同一个）。记 `LastError`（截断到列宽）+ 指数退避（5s 起、×2、封顶 60s、+0~30% 抖动，与 `GenerateExponentialBackoff` 同形），`RetryCount >= MaxRetryCount` 才置 `Status=3` + Error。`OperationCanceledException`（停机）**直接上抛、不消耗重试次数**，租约到期后自然被重新认领。
- **租约兜底**：`LeaseSeconds` 配成 0 或负数一律 `Math.Max(1, ...)` 取 1 秒 —— 租约 0 秒 = 认领的瞬间就过期，多个实例会把同一条消息翻来覆去地投。
- **at-least-once，不是 exactly-once（有意的）**：崩溃 / 租约过期会导致重复投递，这是模式固有的。消费端幂等由业务代码自己负责 —— 框架另给了可选的 Inbox 当工具（见下），但不强制使用。
- **配置**：`VivOptions.OutboxOption` 为 `null` = 不启用。配了 `OutboxOption` 却没有 `NanaOption` / `DatabaseOption` → **启动即抛**（投递的是跨进程事件，缺 MQ 配置根本发不出去；发件箱要靠业务主库原子地存下待发消息）。`AutoCreateTable` 默认 `true`，DDL 本身幂等（`IF OBJECT_ID ... IS NULL` / `CREATE TABLE IF NOT EXISTS`），多实例并发启动安全；关掉走纯手工建表。

#### Inbox（消费端幂等）

- **`IVivInbox` / `InboxStore`**，表 `VivInboxMessage`（`(ServiceName, MessageId)` 复合主键 + `AcceptedAt`）。消费者在同一条业务事务里调 `TryAcceptAsync(messageId)`：首次 `true`、重复投递 `false`（靠唯一约束冲突判定，不是先查后插）。写入走 `ExecuteSqlAsync` 所以并入调用方当前事务 —— 没有外层事务时依然落库（有持久性、没有与业务写的原子性）。`ServiceName` 取入口程序集名，与 `VivConsumer` 那把消费锁的口径对齐。
- **写入端可选，清理端不是 —— 表由框架写就由框架保证它不会无限涨**。`InboxDispatcher : BackgroundService`（同理注册 `IHostedService`），`InboxOptions` 三个旋钮：`RetentionDays`（默认 7，0 或负数 = 不清理）、`BatchSize`（默认 1000）、`CleanupIntervalMinutes`（默认 60 —— 幂等表慢增长，不像发件箱那样要求低延迟，没必要每几秒扫一次）。
- **⚠️ 清理是独立的一条循环，不是折进 `OutboxDispatcher`**：两者启用条件不同 —— Inbox 只要配了 `DatabaseOption` 就注册（`VivRegister.RegisterInbox`），而 `OutboxOption` 未必配（仓库里 6 个有库的服务只有 1 个配了）。折进去的话其余服务永远不会清理，而且是**静默的**，表只会涨。
- **⚠️ 保留期同时就是去重窗口**：行被删掉之后，同一条消息再被投递就会被当成新消息重新处理。所以 `RetentionDays` 要长于「同一条消息最晚可能被重投」的时间窗 —— 消费端退避重试、`RedeliverAsync` 的 `2×(n+1)` 分钟递增、发件箱自身的重试与租约过期重投，得叠起来算。人工把 `Status=3` 的发件箱行捞回来重投属于框架管不到的路径，那种情况本来就不是自动幂等能覆盖的。
- **`InboxOption` 允许缺席**（与其它子配置不同，那些是「为 null = 不启用」）：节点没配就用默认值照常清理，要关掉把保留期配成 0。所以清理器直接注入 `InboxOptions` 而不是 `IOptions<InboxOptions>` —— 节点缺席时后者解析不到，会把宿主直接拖垮；`InboxRegister` 里 `?? new InboxOptions()` 那行就是兜底。
- **清理 SQL 不能照抄发件箱**：表是复合主键，没有单列 `Id` 可供 `Id IN (SELECT ...)` 圈批。SQL Server 走 `DELETE TOP (@BatchSize)`；PostgreSQL 没有 `DELETE LIMIT`，走行值 `(ServiceName, MessageId) IN (SELECT ... LIMIT @BatchSize)`。建表脚本另给 `AcceptedAt` 配了独立索引（复合主键帮不上清理的忙）—— DDL 整份幂等，对已建过表的库也会在下一次启动时补上索引。
- **不做（范围外）**：**延迟入队**（`EnqueueAsync(TimeSpan, T)` —— 现成的 `PublishDelayAsync` 已覆盖「发出去但不立刻到」）。（**Worker 侧类级 `[VivUnitOfWork]` 已补**，见 `### Unit of Work` 的「Worker / 消费者」那条。）

### Database (Momo)

`MomoDatabaseContext` (implements `IMomoDbContext`) uses EF Core for small operations and Dapper for bulk queries (threshold: `EFMaxCount`). `EFAppContext` is created as either read or write — reads randomly select a slave connection, writes always use the master. Entities are auto-scanned via `DatabaseOption.EntityTypeOptions`。**访问失败抛 `VivConnectionException`**（记日志后包装，API 过滤器映射 `-501 DatabaseError`，客户端 Message 用枚举固定文案「数据库操作异常」，实体 JSON / 底层详情只进日志）；`Insert`/`Update`/`Delete` 的 `false` 只表示语句成功但影响 0 行（或入参为空）。`Exist`/`Count`/`Find` 遇库故障不再返回 false/default/-1。`OperationCanceledException` 原样冒泡。回滚失败只记日志，避免掩盖原始异常。`DataAccessCacheBase` Redis 故障当作 miss 回源数据库。

### Schema sync（按实体生成建表/改表 SQL）

**不要再手写 DDL** —— `Viv.Momo/Sync/SchemaSynchronizer.cs` 是一条既有的完整流水线（反射 → 预期 Schema → 查 `INFORMATION_SCHEMA` → Diff → DDL），SQL Server 与 PostgreSQL 双方言：

| 步骤 | 方法 |
|---|---|
| 扫 `EntityTypeOptions` 配置的命名空间里的 `IEntity` 实现 | `ScanEntityTypes` |
| 反射 `[Table]`/`[Column]`/`[Key]`/`[StringLength]`/`[NotMapped]`/`[DatabaseGenerated]`/`[Precision]`，自动跳过导航属性 | `BuildExpectedSchema` |
| 查 `INFORMATION_SCHEMA.TABLES` + `COLUMNS` | `FetchActualSchemaAsync` |
| 表名/列名去下划线 + 忽略大小写匹配；列比类型 + 可空性 | `Diff` |
| `CREATE TABLE` / `ADD COLUMN` / `ALTER COLUMN` / `DROP TABLE` / `DROP COLUMN` | `GenerateDdl` |
| 人读的差异报告（`+` / `-` / `~`） | `GenerateReport` |

入口是 `IMomoDbContext.SyncTableAsync(allowDrop, allowAlterColumn)` —— **两个门都默认关**，所以默认行为只有「建缺失的表、加缺失的列」，不改不删。

**启动钩子（配置驱动）**：`DatabaseOptions.SyncTableOnStartup`（默认 `false`）打开时，`RunVivApi` / `RunVivWorker` 在 `VivLocator.Initialize` 之后、开始接请求之前调一次（`VivStartupSchemaSync`）。`RunVivGateway` 不调（网关无库）。**已给 6 个有实体的服务打开**：Apex.Api / Apex.Worker / DeepRed.Api / DeepRed.Worker / Herta.Api / Herta.Link —— 开发期实体是唯一事实来源，DB 跟着走，不必手写 DDL。同步失败只记 Error 不阻塞启动（与 `OutboxDispatcher.StartupAsync` 同取舍）。生产期建议关掉，交给迁移脚本控制变更时机。

- **🔴 主键判定必须含 `Id` 约定**（`IsPrimaryKeyProperty`）：全仓实体一律继承 `EntityBase`，`[Key]` 只标在基类的 `long Id` 上（EF 也靠约定认主键）。**只认特性、或只认约定，都不可取**：约定那条不能省，否则一旦有实体脱离 `EntityBase` 就生成出**一个主键都没有**的表，而 `_primaryKeys = ["Id"]` 那套 Id 定位全靠它；特性那条也不能省，否则显式 `[Key]` 的非 `Id` 主键（如 `SyncAttributedRow`）会漏。**刻意不实现 EF 的 `{类名}Id` 约定** —— `AtUserRoleRelation.UserId` 这类是外键，按那个约定认会把外键标成主键。
- **🔴 `GenerateDdl` 默认不发 `ALTER COLUMN`**（`DiffType.Modified`，需构造时显式 `allowAlterColumn: true`）：这个判据对现有库**几乎全是误报** —— 预期侧按 `nonPkNullable = true` 认为「除主键外全 NULL」、string 无 `[StringLength]` 就是 `nvarchar(max)`，一比对就把有长度约束的字符串列判成要放宽成 `max`、把 NOT NULL 列判成要去掉。**而 `allowDrop` 那条门管不到这里**（它只清 `Deleted`，不清 `Modified`）。`SyncTableAsync` 会把跳过的 ALTER 逐条记日志（静默跳过 = 被当成「同步成功了」）。
- **🔴 主键只发一条定义**：原先是「内联 `PRIMARY KEY`」+「表级 `CONSTRAINT PK_...`」两条并出（PG 直接 `multiple primary keys` 报错），现在只留**表级具名**那条 —— 具名的后续可定位可删。
- **可空列不显式输出 `NULL`**：`GenerateCreateTable`/`GenerateAddColumn` 只在 `!IsNullable` 时加 `NOT NULL`。省略即可空是两种方言的默认值。
- **不做（范围外）**：索引 / 唯一约束 / 外键的生成（`Diff` 只比列）；`SyncTableAsync` 第 1 步的 `EnsureCreatedAsync` 对**非空库是 no-op**（注释「创建数据库中不存在的表」是错的，真正建表的是后面 Diff 出的 `NewTables`），是个误导性的死步骤。

### Entity audit（`ICreatedAt` / `ICreatedBy` / `IUpdatedAt` / `IUpdatedBy`）

**创建 / 更新 的时间与人由框架自动盖章**，业务代码一行都不用写。与租户隔离同族：都是数据层的自动填充。

- **四个单字段能力接口，逐个 opt-in**（`Viv.Momo/Interface/`，与 `ITenant` / `ISoftDeleted` 同族的能力接口、**不是基类**，`EntityBase` 一字未动）：实体**有几个字段就实现几个接口**，没实现的一律不碰。**刻意不是一个大 `IAudited`** —— 不是所有实体都要完整四件套（纯日志表可能只要创建时间，关系表可能只要创建人）。
- **属性类型必须逐字是 `DateTime?` / `long?`**：C# 要求实现者与接口的属性类型**完全相同**，非空 `DateTime` 直接 **CS0738** 编译不过。可空是**有意的** —— 行可能是手工 SQL / 导入 / DB 默认值造出来的，那时 `null` 比 `0001-01-01` 容易发现得多。本次已把 16 个实体的 30 处 `DateTime` 统一成 `DateTime?`（改之前实测**业务代码零消费者**，所以是纯类型改动）。
- **填充挂在 `MomoDatabase`**（唯一入口，没新增机制）：`AutoSetInsertValue`（Id + TenantId + **四件套一起盖**，否则「只插不改」的行更新时间永远是空）/ `AutoSetUpdateValue`（**只**盖 `Updated*`）。判据是 `entity is ICreatedAt` 运行时判断 —— 泛型约束仍是 `where T : IEntity`，全仓四十来个实体只有一部分有四件套，收紧约束会让其余编译不过。两者都受 **`IsAutoSetValue`** 门控（该开关的语义就是「别碰我的值」）。
- **操作人取 `IVivContext.UserId`（人），不是 `SubjectId`（租户/组织/公司主体 —— `MomoDatabase.TenantId` 取的那个）**。`CurrentUserId` 是**属性**、调用时读，**不在构造时缓存** —— 与 `TenantId` 完全同一读法（构造时冻结会让 Wolverine 那条路整条读到 0）。无登录上下文（Worker / 消息消费 / 后台任务，`UserId == 0`）返回 **`null` 而不是 `0`**，否则审计列里混进一堆 0，跟真实存在的 `UserId = 0` 分不开。
- **🔴 `Update` 的创建信息保护（本次修的既有潜伏 bug）**：4 处 Update 走的都是 `Entry(existing).CurrentValues.SetValues(entity)`，而 `SetValues` 会把入参实体的**全部**映射列无差别覆盖到被跟踪实体上 —— 入参上 `CreatedAt`/`CreatedBy`/`TenantId` 通常是 `default`，于是**每次 Update 都把创建信息冲成 `NULL`、把行搬到租户 0 去**（后者更毒：全局查询过滤器会让行在业务侧直接「消失」）。审计字段真正开始写入之前它俩都是**潜伏**的（没人写过，冲掉了也看不出来）。修法是 **`CopyProtectedValues(existing, entity)` 在 `SetValues` 之前**把库里那份补回入参，之后连同盖章的 `Updated*` 一起 `SetValues`。
- **⚠️ 批量 Update（> `EFMaxCount`）走的是同一形状的另一个实现，必须单独挡**：`BuildUpdateSqlList` 反射**全部**公开属性拼 `UPDATE ... SET col = CASE Id WHEN ... END`，而且它**根本不加载库里的那一份** —— 没有可补的来源。所以那边改成按 **`IsProtectedColumn`** 直接**跳过**这几列（`ELSE {dbField} END` 自然保留原值）。判据只能问 `typeof(T)` 是否实现了该契约（没有实例可 `is`）。两个机制**共用同一份清单**，改一处要改两处。
- **`Worker` 侧没有触发点、也没有软删除的份**（明确范围外，不是遗忘）：
  - **EF `SaveChanges` 拦截器方案不可行** —— 批量 Insert（≥200）、批量 Update、**全部 `SoftDelete`** 都走 Dapper 原生 SQL，压根不经过 `SaveChanges`，覆盖不全。别走这条。
  - **软删除的 `DeletedAt` 是数据库端填的**（`SqlMagic.GetSoftDeleteSql` 用 `NOW()` / `GETDATE()`），绕开实体；`ISoftDeleted` 里也**没有 `DeletedBy`**。要补是独立改动。
- **标记现状（`src/Vivian/Viv.Entity/Database/`，41 个实体）**：
  - **Apex 24 个 —— 按「实体现有字段」逐个标**：21 个完整四件套；`AtFileRecord` 只有 `CreatedAt` → 仅 `ICreatedAt`；`AtUserRoleRelation` 只有 `CreatedAt` + `CreatedBy` → 仅这两个（**这两个偏门实体正是「拆四个接口」而非一个大 `IAudited` 的理由**）；`AtUserBind` 一个审计字段都没有 → **不标**。
  - **Herta 16 个 `Et*` —— 原先一列都没有，本次整体新增四件套**（属性 + 接口）。它们都实现 `ITenant` / `ISoftDeleted`，是唯一会被 `CopyProtectedValues` 的租户保护照到的实体（Apex 一个都不实现 `ITenant`）。
  - **DeepRed 只有 `VtUser` 一个实体，同样没有审计列，本次未动。**
- **Herta 那 16 个实体「加了属性就等于加了列」**：EF 把公开属性全部映射成列，DB 里没有对应列时**每次读写都直接 `Invalid column name 'CreatedAt'`**（不是降级、不是忽略）。**Herta 的表还没建过**，`SyncTableOnStartup` 已打开 → 下次启动 16 张 `Et*` 表连那 4 列一起建出来，不用手写 DDL。**Apex 才是真正的风险面**（23 个实体带审计列）：若它的表**已经建过**（建表时还没有这些字段），启动同步会走 `ADD COLUMN` 补上 —— 补列属于默认路径，不需要额外放宽 `allowAlterColumn`。反过来，**表已存在时改实体的列类型 / 可空性**属于 `Modified`，默认被拒。
- **⚠️ 测试覆盖的边界**：`AutoSetInsertValue` / `AutoSetUpdateValue` / `CopyProtectedValues` 是纯内存逻辑，有单测钉死（`Viv.Momo.Tests/AuditValueTests.cs`，探针 `MomoAuditSut` 在 `Viv.Fakes/Audit.cs`）。但**真正的落库路径（`SetValues` + `SaveChanges`）需要数据库，CI 没有** —— 那一段没有被自动化覆盖，别把这些单测当成端到端验证。

### Multi-tenancy

`VivContextMiddleware` reads `Viv_AppId`, `Viv_TenantId`, `Viv_UserId` from HTTP headers and hydrates `IVivContext` (scoped, backed by `AsyncLocal<long>`). **数据层租户隔离**（框架自动，业务代码无需手写租户条件）：

- **EF 全局查询过滤**：`EFAppContext.OnModelCreating` 对所有 `ITenant` 实体加 `HasQueryFilter`——`e => 无请求上下文 || e.TenantId == 当前租户`。覆盖全部 EF 谓词查询（`Exist`/`Count`/`SingleOrDefault`/`FirstOrDefault`/`FindList` 及 Async）和 `ExecuteDeleteAsync`。表达式捕获 `IVivContextAccessor` 常量（单例，静态 AsyncLocal），每次查询重求值，跨请求正确。
- **无上下文不过滤**：`tenantAccessor.Current == null`（后台消费者等无请求场景）时不过滤，避免静默破坏后台任务；HTTP 请求路径由 `VivContextMiddleware` 保证必有上下文，因此请求侧跨租户读取被拦截。
- **Dapper 单实体/删除**：`Find<T>/FindAsync<T>`（按 Id）、`Delete<T>/SoftDelete<T>`（谓词/Id/批量）在 `T : ITenant` 且当前有租户时追加 `AND [TenantId] = @TenantId`（`SqlMagic.AppendTenantFilter`，删改同样按租户隔离）。
- **逃生口（框架不自动加租户）**：接受原生 SQL 字符串的重载（`FirstOrDefault<T>(sql,…)`/`FindList<T>(sql,…)`/`FindScalar`/`Page`）由调用方自持 SQL，框架无法安全改写，跨租户风险由调用方负责。
- **Redis 租户库**：`TenantIdAllocator.AllocateDbIndex` 在**调用时**解析当前租户（`VivLocator.GetService<IVivContextAccessor>().Current?.SubjectId`），非构造时缓存，避免单例 allocator 被首个请求固化。
- **AsyncLocal 流进后台线程（约束）**：`VivContextAccessor` 的租户上下文存在静态 `AsyncLocal`，会随 ExecutionContext 流入 `Task.Run`/`new Thread`。请求中 fire-and-forget 的后台任务会**继承发起请求的租户**，请求结束后仍带着旧租户跑 → 后台跨租户（框架"无上下文不过滤"兜底此时不生效，因为继承的是非空租户）。业务代码如用 `Task.Run`/`new Thread` 做租户敏感操作，需自行 `ExecutionContext.SuppressFlow()` 或在任务内显式清除/重设租户上下文。

### Unified API response

Controllers return `VivApiResult` (implements `IActionResult`) — a `{ Code, Message, Data }` envelope. `Newtonsoft.Json` is used for serialization with `VivContractResolver` and `yyyy-MM-dd HH:mm:ss` date format. Model validation is suppressed via `SuppressModelStateInvalidFilter = true`; validation is handled by the `RequestFilterAttribute` pipeline instead.

**HTTP 状态码原样返回（白名单）**：`VivApiResult.ExecuteResultAsync` 默认强制 HTTP 200；如需原样返回非 200（301/302 重定向、304、404 等），业务在返回前先 `Response.StatusCode = xxx`（重定向再写 `Response.Headers["Location"]`）再返回 `VivApiResult`，`ExecuteResultAsync` 会按 `VivRunDefine.AllowedHttpStatusCodes`（`Viv.Engine`）白名单保留该状态码。白名单外状态码仍强制 200；直接用框架结果类型（`Redirect(...)`/`StatusCodeResult` 等）本就透传，不受此约束。中间件逃生口 `context.SetApiResponseAsync(code, httpStatus)` 同样按该白名单门控，白名单外状态码强制 200。

### Operation logging (Elysia)

- **触发（两条路径）**：① action 标 `[OperationLog(module, operation, params int[] codes)]`（`Viv.Elysia.Attributes`，**声明式 opt-in**，无需业务调 SetLog）② 业务代码 `ElysiaLogContextAccessor.SetLog(module, operation, description?, isRecord?)` 程序式声明。两条路径都需 `AddElysiaFilter()`（`ElysiaApiExtensions`，替换原来手动 `Filters.Add<RequestFilterAttribute>()`，同时注册 `RequestFilterAttribute` + `OperationLogFilterAttribute`）。
- **入口优先级（有 Current 优先）**：filter 入口先看 `ElysiaLogContextAccessor.Current`——已有值（外部已 Set/SetLog）**不覆盖**；为 null 才读 action 上的 `[OperationLog]` 特性播种（`Module/Operation` + `IsSet=true`）；无特性则预置空容器等业务 SetLog。`opCtx` 在 `await next()` 后被清空时兜底回退读特性。
- **AsyncLocal 预置容器（关键机制）**：AsyncLocal 只从父流向子，action 里 `SetLog` 的写入跨 `await` 流不回 filter 续段——`OperationLogFilterAttribute` 在 `await next()` 前先 `ElysiaLogContextAccessor.Set(new OperationLogContext())` 预置可变容器，`SetLog` 改的是容器**字段**（引用不变），filter 续段读同一引用即拿到结果。`OnActionExecutionAsync` **`finally` 一律 `Clear()`**（跳过发布 / 异常同样清），避免 keep-alive 或线程复用把 `Current` 带到下一请求，导致特性播种被跳过、串号或漏记。
- **`IsSet` 门控**：`OperationLogContext.IsSet` 区分「未声明记录意图」（无特性且业务没 SetLog → 跳过发布）与「明确不记录」（`isRecord:false`）——避免误发布未标注操作日志的请求。
- **状态码门控（仅特性播种时生效）**：result 为 `VivApiResult` 且业务信封码 `Code` 不在 `Codes` 内 → 不记录；`Codes` 默认 `[200]`（只记成功，`ApiResultCode.Success=200`）。
- **Description 缺省**：特性播种且业务没设 Description → 取 `result.Message`（特性注释：以返回结果的 Message 为日志内容）。
- **链路**：filter 发布 `UserOperationLogEvent`（`Viv.EventContracts/Apex/Logging`），`UserOperationLogConsumer`（`Viv.Apex.Worker/Consumers/Logging`）消费落库。
- **worker/非 filter 流程**：无预置容器时 `SetLog` 自建独立上下文（`IsSet=true`），不依赖 filter。

### CLI commands (Viv.Cli)

Create a command by implementing `AsyncCommand` and decorating it with `[VivCommand]`:

```csharp
[VivCommand("migrate", "执行数据库迁移")]
public class Cmd_Migrate : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context) { ... }
}
```

- Drop into `Commands/` — auto-discovered at startup.
- Support aliases: `[VivCommand("clear, cl", "清除屏幕")]` → help displays `清除屏幕（别名: cl）`.
- Built-in commands: `clear` (aliased `cl`) always available.
- Interactive input via `InputMagic.GetInput()` / `.Confirm()` / `.Select()`; formatted output via `Out.Println()` / `.PrintlnError()` / `.PrintlnFormatJson()`.
