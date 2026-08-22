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

The solution splits into two top-level namespaces: **Banshee** (framework) and **Vivian** (application). A third namespace, **Test**, holds CLI tooling.

---

### Banshee — the Viv framework (`src/Banshee/`)

| Project | Role |
|---|---|
| `Viv.Contracts` | Base interfaces (`IVivContext`, `IDependency`) and shared enums |
| `Viv.Delusion` | Utility library — `TypeScanMagic` (assembly type scanning), `ObjectMapper` (Emit + Expression-based), encryption, common extensions |
| `Viv.Aoi` | DI bridge — `VivLocator` wraps both MS DI and Autofac `ILifetimeScope`; static service resolution for non-injection scenarios |
| `Viv.Engine` | **Core wiring hub** — `VivEngine.LoadVivConfig(builder.Configuration)` binds the `VivOptions` node from appsettings.json into `VivOptions`; `VivRegister` wires every Banshee subsystem into DI via `AddViv()`; provides `VivApiExtensions` / `VivWorkerExtensions` / `VivStartGatewayExtensions` for one-liner startup |
| `Viv.Log` | Logging — Serilog or no-op backend, configurable per `LogType`; Seq integration |
| `Viv.Momo` | Database — `IVivDbContext` backed by **EF Core + Dapper** hybrid; read/write connection routing via `EFAppContext`; supports PostgreSQL and SQL Server |
| `Viv.Nana` | Messaging — `IVivEventPublisher` / `NanaEventPublisher` (publish + delayed publish); `VivConsumer<T>` base class; built on **Wolverine + RabbitMQ**; Saga support with EF Core state persistence |
| `Viv.Redis` | Redis cache — `IRedisService` with pluggable DB allocation (`DbSelectorType`) |
| `Viv.Sandrone` | Cloud integrations — JWT `ITokenService`/`JwtTokenService`（TokenOption 对称密钥）、S3 `IS3Service`/`VivS3Service` |
| `Viv.Echo` | Service-to-service communication + **框架级 gRPC 宿主**（`Viv.Echo.Grpc`）— HTTP + gRPC 客户端 `VivGrpcInterceptor`/`AddVivGrpcClient`（支持服务发现）、服务端 `VivGrpcServerInterceptor`（x-viv-* 头水合 `IVivContext`）/`AddVivGrpcServer`/`AddVivGrpcKestrel`/`VivGrpcDiscovery`（自动发现 `[BindServiceMethod]` 实现类 + 注册 + 反射映射；REST + gRPC 分端口，见下） |
| `Viv.Clockwork` | Background scheduling — `TickerQ` integration for cron/interval job execution with dashboard |
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
| `Viv.Aspire.Gateway` | **YARP** reverse proxy — 由框架层 `VivStartGatewayExtensions` 启动；限流、输出缓存、JWT 解析（`TokenOption` 对称密钥，**只解析不强制**）、认证后向透传 `x-viv-*` 上下文头（appId/subjectId(=TenantId)/userId/serviceName）；路由从 Aspire 服务发现自动生成，下游服务自行鉴权 |
| `Viv.Aspire.ServiceDefaults` | OpenTelemetry tracing/metrics, `/health` + `/alive` endpoints, service discovery, HTTP resilience |

### Test (`src/Test/`)

Unit test suites, one per framework project — `Viv.Delusion.Tests`、`Viv.Engine.Tests`、`Viv.Momo.Tests`、`Viv.Nana.Tests`、`Viv.Redis.Tests`、`Viv.Sandrone.Tests`。CI（`.github/workflows/dotnet.yml`）会跑全量测试并上报覆盖率。

---

## Key Patterns

### Startup: one-liner API & Worker & Gateway

Do **not** copy `Program.cs` boilerplate. Use the framework extension methods:

```csharp
// ── API ──────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivApi("Viv XXX API", mvc => mvc.Filters.Add<RequestFilterAttribute>());
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
- `AddVivGateway` handles config load、Autofac、`AddViv()`、JWT 解析（读 `TokenOption` 对称密钥，**只解析不强制**）、CORS、OutputCache、RateLimiter、`AddReverseProxy().LoadFromMemory(...)`（路由/集群从 Aspire 服务发现自动生成）；`RunVivGateway` 管道：Build → VivLocator → WebSocket → CORS → OutputCache → RateLimiter → Authentication → Authorization → **上下文头透传**（先剥离客户端伪造的 `x-viv-*` 头与 `x-request-token`，再剥离 query 里客户端直传的身份参数 `tenantId/userId/appId`，认证后从 token claims 回填 `x-viv-appId`/`x-viv-subjectId`(=TenantId)/`x-viv-userId`/`x-viv-serviceName` 并 HMAC 签名写 `x-request-token`）→ `MapReverseProxy` → Run。**路由不带 AuthorizationPolicy** —— 网关不鉴权，下游服务自己用 `[Authorize]` 控制。
- **SignalR/WS 认证**：网关 JwtBearer 支持 `access_token` 查询参数认证（`JwtBearerEvents.OnMessageReceived`，SignalR 升级请求无法带 `Authorization` 头）；客户端经 `/ws/{短名}/...` 需带有效 JWT 才能获得身份（经 `x-viv-*` 头下传），下游 hub（如 `ChatHub`）读 `RequestTokenResolver.GetContextFromHeaders` 取已验身份，**无身份即 `Context.Abort()`**——不再信任客户端 query 直传的 tenantId/userId/appId。
- **`x-request-token` 防重放**：签名载荷含 unix 时间戳（token 格式 `{unixSeconds}:{base64Sig}`），下游验签时校验 ≤300s，超时/旧格式（无冒号）一律拒绝——截获签名头组也不能无限期重放冒充。
- **头部契约/白名单集中存放**：上下文头名（`x-viv-appId`/`x-viv-subjectId`/`x-viv-userId`/`x-viv-serviceName`/`x-request-token`）**单一来源定义在 `VivHeaderContract`（`Viv.Contracts`，gRPC/HTTP 跨层共用，Echo/ServiceProxy 拦截器读它）**，`VivRunDefine`（`Viv.Engine`）以别名引用保持既有调用点不变；HTTP 状态码白名单仍在 `VivRunDefine`。网关/`RequestTokenResolver`/`VivApiResult` 跨层共用同一来源。
- **内部签名密钥 `EnvOption.InternalToken`**：`RequestTokenResolver.GetInternalSecret()` 优先取 `VivEngine.VivOptions?.EnvOption?.InternalToken`（appsettings.json 的 `VivOptions.EnvOption` 节点，网关与**所有服务必须配同一个值**，32 位随机 hex），缺省回落到 `TokenOption.SecretKey`（向后兼容，旧部署无需改配置即可互通）。匿名服务（两者皆 null）不验签，按原行为信任头——只用于无租户数据场景。
- **路由自动生成**：`VivGatewayRouteBuilder.Build()` 从 Aspire 注入的 `services__*` 环境变量（`WithReference`）为每个服务生成 3 条路由 + 1 个集群，**零手写 JSON**（无 `viv.yarp.json`）。新增 API 只需在 AppHost 加 `WithReference`：
  - `/api/{短名}/{**catch-all}` → `/api/{**catch-all}`（标准 API，`PathPattern` 匹配替换吃掉中间段）
  - `/docs/{短名}/{**catch-all}` → `/{**catch-all}`（Scalar 文档）
  - `/ws/{短名}/{**catch-all}` → `/{**catch-all}`（SignalR / WebSocket 透传）
  - **短名 = 服务名 `split('-')[1]`**（`viv-apex-api` → `apex`）；第二段冲突时（`viv-herta-api` 与 `viv-herta-link` 都是 `herta`），`-api` 保留基础短名，其余服务拼接剩余段（`herta`+`link` → `hertalink`），保证集群 ID 唯一。
- **下游自鉴权**：`AddVivApi` 自动注册 JwtBearer（读 appsettings.json 的 `VivOptions.TokenOption`），控制器用 `[Authorize]` 逐个控制；`TokenOption` 为 `null`（如 hertalink）则跳过注册保持匿名——此时 `RunVivApi` **不调用** `UseAuthentication/UseAuthorization`（否则匿名服务首请求抛 `IAuthenticationSchemeProvider` 无法解析）。网关只解析透传，不拦截未登录请求。
- **网关代理文档**：`/docs/{短名}/{**catch-all}` 路由（如 `/docs/apex/scalar/`）把各服务 **Scalar** 文档经网关透出，欢迎页服务标签即指向此（不跳服务自身地址）。前提：Scalar.AspNetCore ≥2.16 生成的 HTML 用相对路径（`openapi/v1.json`、`./scalar.aspnetcore.js`）且自带子目录 basePath 计算，`PathPattern: "/{**catch-all}"` 去掉 `/docs/{短名}` 即可；链接需带尾斜杠 `/scalar/`，否则下游 302 后浏览器会请求网关根路径 `/scalar/`。路由自动生成，欢迎页零映射。
- **JWT SecretKey ≥ 32 字节**：IdentityModel 8.x 的 HS256 强制要求 ≥256 bit（`IDX10720`），且 `TokenOption` 必须在**所有会签发/验证 token 的服务间保持一致**（含网关）。
- `AddServiceDefaults()` and `MapDefaultEndpoints()` are **caller-side** Aspire concerns; the framework does not reference Aspire.

### Configuration: `VivOptions` node in `appsettings.json`

Every API and Worker project carries a `VivOptions` node in its `appsettings.json`. `VivEngine.LoadVivConfig(builder.Configuration)` binds it via `configuration.GetSection("VivOptions").Get<VivOptions>()`（MS ConfigurationBinder）and sets the static `VivEngine.VivOptions` snapshot for runtime consumers. Sub-sections drive all subsystem wiring:

> **环境变量覆盖（走标准配置链后生效）**：`VivOptions__*` 可覆盖任意节点（如 `VivOptions__EnvOption__InternalToken`、`VivOptions__TokenOption__SecretKey`）——此前 `LoadVivConfig` 绕过 IConfiguration 时 env 对 viv 配置无效。各服务 appsettings.json 目前仍直接携带凭据（迁移现状，非本次引入）；要彻底抽离需配合 env/user-secrets/Key Vault 提供程序覆盖。

| Section | Drives |
|---|---|
| `EnvOption` | Environment (`Env`/`ServiceName`/`MachineId`/`ServiceType`) + `InternalToken`（x-request-token 内部签名共享密钥，网关与所有服务同值） |
| `DIOption` | Type-scanning rules for Service/Repository auto-registration |
| `LogOption` | Logging backend (Serilog → Seq) |
| `CacheOption` | Redis connection + memory cache toggle |
| `DatabaseOption` | Database type, read-write split, entity scan targets |
| `NanaOption` | RabbitMQ host/port/credentials, consumer type list, retry count, Saga DB |
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

- **Producer:** `IVivEventPublisher.PublishAsync<T>()` / `PublishDelayAsync<T>(TimeSpan, T)` — messages must extend `NanaEvent`（not `VivMessage`）。`NanaEventPublisher` 内部包成 `NanaEnvelope<T>`（含 `IVivContext` 快照 `Context`，租户上下文随消息透传），调 `IMessageBus.PublishAsync(envelope)` / `ScheduleAsync(envelope, delay)`。
- **Consumer:** Extend `VivConsumer<T>`, override `ReceiveMessageAsync()` — return `SubscribeResult` 指示成功或重投。基类 `HandleAsync(NanaEnvelope<T>, CancellationToken)`（Wolverine handler 约定，`Discovery.IncludeType` 显式注册）将结果映射：`Success` → 确认；`Requeue` → 抛 `VivRequeueException`（走全局重试策略）；失败 → 记日志丢弃。
- **延迟重投（`VivConsumer.RedeliverAsync`）**：业务失败想延迟再试时调用 `RedeliverAsync(envelope, delay)`，把**原信封**经 RabbitMQ 延迟交换机在 delay 后重投 fanout（各订阅服务各收一份，谁爱消费谁消费，分布式锁保证只执行一次）。`NanaEnvelope` 加 `ReDeliverCount`/`DelaySecond` 字段随信封透传；`IVivEventPublisher` 新增**信封版** `PublishDelayAsync(TimeSpan, NanaEnvelope<T>, ...)` 直接 `ScheduleAsync` 原信封——内容版重载会新建信封，丢 MessageId/ReDeliverCount/DelaySecond/Context（锁 Key 与计数无法存活）。重投前 `ReDeliverCount+1`，超过 `NanaOptions.RetryCount` 上限返回 Failed(IsRequeue:false) 丢弃不回队。上限经 `VivConfigRegistry.Get<NanaOptions>()` 静态取（`NanaRegister.Initialize` 已把 NanaOptions 放进注册表，**无需 DI 注入**）；`VivConsumer` 构造注入 **`VivConsumerDependency`**（聚合 `ILoggerContract`/`IVivContext`/`IVivEventPublisher`，`: IDependency` 经 `AutoDependencyRegister` 自动注册 **AsSelf + Scoped**，子类构造 `: base(dependency)` 透传即可）。**拿锁失败自动重投（可选，opt-in）**：producer 在 `NanaEvent` 上设 `LockFailShouldRetryDeliver=true`（随内容序列化、重投副本天然继承）且 `DistributedLockException.InnerException == null`（**纯拿锁失败**——业务在锁内抛异常/Redis 异常都有 Inner，不触发自动重投，业务自己处理）时，基类按 `2×(ReDeliverCount+1)` 分钟递增延迟重投，成功重投原消息 ack（不打印丢弃）；同样受 `RetryCount` 上限约束，达上限才丢弃。
- **配置（`AddVivWolverine`，`AddViv()` 内调用）：**
  - `UseRabbitMq(amqp://user:pass@host:port/vhost).AutoProvision()` — 队列/交换机自动声明（**先清理旧 MassTransit 拓扑遗留的队列**，否则 `406 PRECONDITION_FAILED`）。
  - **发布订阅拓扑**：发布侧 `PublishMessage<NanaEnvelope<T>>().ToRabbitExchange({EventName}Exchange)`（fanout 交换机）；消费侧 `ListenToRabbitQueue({EventName}Queue.{ServiceName})` + `transport.BindExchange({EventName}Exchange, ExchangeType.Fanout).ToQueue(queue)`——每个订阅服务建一条**独立队列**绑到交换机，**各收一份**（`NanaRegister.GetExchangeName`/`GetConsumerQueueName` 约定；`ServiceName` = 入口程序集名，同服务多实例共享队列轮询）。**"只执行一次"由业务层自己拿 Redis 分布式锁保证**（拿到执行、拿不到丢弃），框架只负责广播。
  - **消费并发/预取调优**：`VivConsumer<T>` 子类可标 `[NanaConsumer(ConsumerCount, PrefetchCount, MaximumParallelMessages)]` 控制该队列的消费通道数（>1 丢失同队列严格顺序）、每通道预取（basic.qos）、端点最大并行；特性缺席回落框架默认 **prefetch=20**（收敛 Wolverine 原生 100，降低崩溃重投放大）、队列 **Quorum**（多副本防丢消息）。`AddVivWolverine` 内**直接写 `RabbitMqQueue` 属性**（`VivWolverineConfigurationExtensions`）——该 fork 的 fluent `PreFetchCount/ListenerCount/QueueType` 是空壳（编译通过但不落盘），必须直写。⚠️ 已存在的 classic 队列不会自动变 quorum，重声明类型不一致会 `406 PRECONDITION_FAILED`，切换前需清掉旧队列。
  - 全局失败策略：`OnException<Exception>().RetryWithCooldown(指数退避 5s 起、最大 60s，共 RetryCount 次).Then.MoveToErrorQueue()`（死信 → `wolverine-dead-letter-queue`）。
  - **EF Saga 持久化**：`NanaOption.SagaConnectionString` 已配且扫到 `VivSagaState` 子类（`TypeScanMagic.ScanTypes<VivSagaState>()`，需 `ForceLoadReferencedAssemblies()` 强制加载业务 Core 程序集）时启用：`opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Lightweight)`（**内联在 options 里**，规避 JasperFx/wolverine#1140 DI 修改 bug；**Lightweight = 无 durable outbox**，默认 Eager 要求数据库消息持久化会抛 "not using Database backed message persistence"）+ `VivSagaDbContext` 映射 `Saga_{SagaTypeName}` 表。
  - **Saga 实体主键**：`VivSagaDbContext.OnModelCreating` 用 `[SagaIdentity]` 标记的属性（如 `OrderSaga.OrderId`）显式 `HasKey`——EF 无法从 Saga 类型推断主键（`Id`/`Version` 都不是约定名），不配置会抛 "requires a primary key"，Wolverine 进而判定无 EF 持久化提供者（"No known Saga persistence provider"）。saga 表（`Saga_OrderSaga`）需预先建好（`EnsureCreated`/迁移）。
  - `TypeLoadMode.Dynamic`（开发默认）需引用 `WolverineFx.RuntimeCompilation`。

### Database (Momo)

`VivDatabaseContext` (implements `IVivDbContext`) uses EF Core for small operations and Dapper for bulk queries (threshold: `EFMaxCount`). `EFAppContext` is created as either read or write — reads randomly select a slave connection, writes always use the master. Entities are auto-scanned via `DatabaseOption.EntityTypeOptions`.

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
- **AsyncLocal 预置容器（关键机制）**：AsyncLocal 只从父流向子，action 里 `SetLog` 的写入跨 `await` 流不回 filter 续段——`OperationLogFilterAttribute` 在 `await next()` 前先 `ElysiaLogContextAccessor.Set(new OperationLogContext())` 预置可变容器，`SetLog` 改的是容器**字段**（引用不变），filter 续段读同一引用即拿到结果。
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
