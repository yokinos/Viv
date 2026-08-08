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

# Run the CLI test harness
dotnet run --project src/Test/Viv.Test
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
| `Viv.Engine` | **Core wiring hub** — `VivEngine.LoadVivConfig("viv.config.json")` deserializes all config into `VivOptions`; `VivRegister` wires every Banshee subsystem into DI via `AddViv()`; provides `VivApiExtensions` / `VivWorkerExtensions` / `VivStartGatewayExtensions` for one-liner startup |
| `Viv.Log` | Logging — Serilog or no-op backend, configurable per `LogType`; Seq integration |
| `Viv.Momo` | Database — `IVivDbContext` backed by **EF Core + Dapper** hybrid; read/write connection routing via `EFAppContext`; supports PostgreSQL and SQL Server |
| `Viv.Nana` | Messaging — `IVivPublisher` / `NanaEventPublisher` (publish + delayed publish); `VivConsumer<T>` base class; built on **MassTransit + RabbitMQ**; Saga support with EF Core state persistence |
| `Viv.Redis` | Redis cache — `IRedisService` with pluggable DB allocation (`DbSelectorType`) |
| `Viv.Sandrone` | Cloud integrations — JWT `ITokenService`/`JwtTokenService`（TokenOption 对称密钥）、S3 `IS3Service`/`VivS3Service` |
| `Viv.Echo` | Service-to-service communication — HTTP + gRPC clients |
| `Viv.Tick` | Background scheduling — `TickerQ` integration for cron/interval job execution with dashboard |
| `Viv.Cli` | **CLI framework** — `VivCliHost` (REPL loop + Spectre.Console.Cli `CommandApp`); `[VivCommand]` auto-discovery; built-in `Cmd_Clear`; `Out` (formatted output) and `InputMagic` (interactive input) utilities |
| `Viv.Forge` | Code generation — compile-time gRPC client generation via `Viv.Forge` |

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
| `Viv.Elysia` | Request validation pipeline — `RequestFilterAttribute`, `RequestValidator<T>`, `AppMemoryCache` |
| `Viv.EventContracts` | Shared message/event class definitions for inter-service messaging |
| `Viv.Sdk` | Shared SDK — gRPC client stubs, shared DTOs |

### Aspire orchestration (`src/Vivian/Viv.Aspire/`)

| Project | Role |
|---|---|
| `Viv.Aspire.AppHost` | .NET Aspire orchestrator — launches all services with dependency ordering |
| `Viv.Aspire.Gateway` | **YARP** reverse proxy — 由框架层 `VivStartGatewayExtensions` 启动；限流、输出缓存、JWT 验证（`TokenOption` 对称密钥）、认证后向透传 `x-viv-*` 上下文头（appId/subjectId(=TenantId)/userId/serviceName） |
| `Viv.Aspire.ServiceDefaults` | OpenTelemetry tracing/metrics, `/health` + `/alive` endpoints, service discovery, HTTP resilience |

### Test (`src/Test/`)

| Project | Role |
|---|---|
| `Viv.Test` | CLI command suite — built on `Viv.Cli`; commands auto-discovered via `[VivCommand]` |

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
builder.AddVivGateway();                      // 读 viv.config.json + viv.yarp.json + viv.ratelimit.json
builder.RunVivGateway(app => app.MapDefaultEndpoints());
```

- `AddVivApi` / `AddVivWorker` handle config load, Autofac setup, `AddViv()`, MVC/filters, CORS, Swagger, and encoding registration.
- `RunVivApi` handles Build → VivLocator → Swagger UI (dev) → middleware pipeline → Run. Accepts an `Action<WebApplication>? configure` for custom endpoints (`UseTickerQ()`, `MapHub()`, etc.).
- `RunVivWorker` handles Build → VivLocator → Run.
- `AddVivGateway` handles config load、Autofac、`AddViv()`、JWT 验证（读 `TokenOption` 对称密钥）、CORS、OutputCache、RateLimiter、`AddReverseProxy()`；`RunVivGateway` 管道：Build → VivLocator → WebSocket → CORS → OutputCache → RateLimiter → Authentication → Authorization → **上下文头透传**（先剥离客户端伪造的 `x-viv-*` 头，认证后从 token claims 回填 `x-viv-appId`/`x-viv-subjectId`(=TenantId)/`x-viv-userId`/`x-viv-serviceName`）→ `MapReverseProxy` → Run。
- **YARP 鉴权路由**：`viv.yarp.json` 的受保护路由用 `"AuthorizationPolicy": "default"`（而非 `"Metadata": { "Authorize": true }` —— YARP 2.3.0 会静默丢弃该元数据，端点不产生 `IAuthorizeData`，鉴权中间件不生效）。
- **网关代理文档**：`/docs/{服务名}/{**catch-all}` 路由（如 `/docs/viv-apex-api/scalar/`）把各服务 **Scalar** 文档经网关透出，欢迎页服务标签即指向此（不跳服务自身地址）。前提：Scalar.AspNetCore ≥2.16 生成的 HTML 用相对路径（`openapi/v1.json`、`./scalar.aspnetcore.js`）且自带子目录 basePath 计算，YARP `PathRemovePrefix` 去掉 `/docs/{服务名}` 即可；链接需带尾斜杠 `/scalar/`，否则下游 302 后浏览器会请求网关根路径 `/scalar/`。路由用 Aspire 服务名当前缀，欢迎页零映射。
- **JWT SecretKey ≥ 32 字节**：IdentityModel 8.x 的 HS256 强制要求 ≥256 bit（`IDX10720`），且 `TokenOption` 必须在**所有会签发/验证 token 的服务间保持一致**（含网关）。
- `AddServiceDefaults()` and `MapDefaultEndpoints()` are **caller-side** Aspire concerns; the framework does not reference Aspire.

### Configuration: `viv.config.json`

Every API and Worker project requires a `viv.config.json` at its root. `VivEngine.LoadVivConfig()` deserializes it into `VivOptions`, whose sub-sections drive all subsystem wiring:

| Section | Drives |
|---|---|
| `DIOption` | Type-scanning rules for Service/Repository auto-registration |
| `LogOption` | Logging backend (Serilog → Seq) |
| `CacheOption` | Redis connection + memory cache toggle |
| `DatabaseOption` | Database type, read-write split, entity scan targets |
| `NanaOption` | RabbitMQ host/port/credentials, consumer type list, retry count, Saga DB |
| `TokenOption` | JWT secret/expiry/issuer |
| `EchoOption` | gRPC + HTTP client enable/disable |
| `TickOption` | TickerQ scheduler config |

### DI: Autofac root + MS DI delegation

`builder.Services.AddViv(vivOptions)` registers all Banshee services into MS DI. The `AutofacServiceProviderFactory` sets Autofac as the root container — all MS DI registrations are delegated to Autofac for resolution.

Business-layer services and repositories are registered via **type scanning** driven by `DIOption` — assembly name, namespace, and class name suffix (e.g., `"ClassNameEndWith": "Service"`).

- **API:** `builder.Host.UseServiceProviderFactory(...)` + `ConfigureContainer(...)`
- **Worker:** `builder.ConfigureContainer(new AutofacServiceProviderFactory(), ...)`

`VivLocator.Initialize()` is called during startup and provides static access for scenarios where constructor injection is unavailable.

### Messaging (Nana)

- **Producer:** `IVivPublisher.PublishAsync<T>()` / `PublishDelayAsync<T>(TimeSpan)` — messages must extend `NanaEvent` (not `VivMessage`).
- **Consumer:** Extend `VivConsumer<T>`, override `ReceiveMessageAsync()` — return `SubscribeResult` to indicate success or requeue.
- **Configuration:** `NanaOption.ConsumerTypes` lists classes to register as MassTransit consumers via `TypeScanMagic`. The Worker's `AddViv()` call automatically wires MassTransit + RabbitMQ with the configured host and retry policy.

### Database (Momo)

`VivDatabaseContext` (implements `IVivDbContext`) uses EF Core for small operations and Dapper for bulk queries (threshold: `EFMaxCount`). `EFAppContext` is created as either read or write — reads randomly select a slave connection, writes always use the master. Entities are auto-scanned via `DatabaseOption.EntityTypeOptions`.

### Multi-tenancy

`VivContextMiddleware` reads `Viv_AppId`, `Viv_TenantId`, `Viv_UserId` from HTTP headers and hydrates `IVivContext` (scoped, backed by `AsyncLocal<long>`). Database operations use `TenantId` for logical row isolation.

### Unified API response

Controllers return `VivApiResult` (implements `IActionResult`) — a `{ Code, Message, Data }` envelope. `Newtonsoft.Json` is used for serialization with `VivContractResolver` and `yyyy-MM-dd HH:mm:ss` date format. Model validation is suppressed via `SuppressModelStateInvalidFilter = true`; validation is handled by the `RequestFilterAttribute` pipeline instead.

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
