# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the entire solution
dotnet build

# Run the Aspire AppHost (orchestrator that launches all services)
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost

# Run a specific API project directly
dotnet run --project src/Vivian/Viv.Apex.Api
```

There is no test runner configured — Viv.Test is a console project (`dotnet run --project src/Test/Viv.Test`).

## Architecture

The solution splits into two namespaces: **Banshee** (framework/infrastructure) and **Vivian** (application/business).

### Banshee — the Viv framework

| Project | Role |
|---|---|
| `Viv.Contracts` | Base interfaces (`IVivContext`, `IDependency`) and shared enums |
| `Viv.Vva` | Utility library — Snowflake ID generators, `TypeScanMagic` (assembly type scanning), encryption, `ObjectMapper`, `VivConfigRegistry` |
| `Viv.Aoi` | DI bridge — `VivLocator` wraps both MS DI and Autofac `ILifetimeScope` |
| `Viv.Engine` | Core wiring — `VivEngine.LoadVivConfig("viv.config.json")` loads all config, `VivRegister` wires every Banshee subsystem into DI, `VivOptions` is the single config root |
| `Viv.Log` | Logging — `IDistributedLogger` with Serilog or no-op backend |
| `Viv.Momo` | Database — `IVivDbContext` provides full CRUD via `VivDatabaseContext`, backed by **EF Core + Dapper** hybrid. `EFAppContext` resolves read/write connections at init time |
| `Viv.Nana` | Messaging — `IVivProducer`/`NanaProducer` (publish + delayed publish), `VivConsumer<T>` base class, all built on **MassTransit + RabbitMQ** |
| `Viv.Redis` | Redis cache — `IRedisService`/`RedisService` with pluggable DB allocation |
| `Viv.Authentication` | JWT tokens via `ITokenService`/`JwtTokenService` |

### Vivian — the application layer

| Project | Role |
|---|---|
| `Viv.Entity` | EF entity classes (Apex domain: `VivClientApp`, `VivClientAppVersion`) |
| `Viv.Elysia` | Request validation pipeline — `RequestFilterAttribute`, `RequestValidator<T>` |
| `Viv.Apex.Core` | Business logic — Services + Repositories (Service/Repository pattern) |
| `Viv.Apex.Api` | Main Web API host |
| `Viv.Herta.Link` / `Viv.Herta.Api` / `Viv.Robin.Api` | Additional API services (template/prototype) |
| `Viv.Toolkit` | CLI tools |
| `Viv.Sdk` | Shared SDK library |

### Aspire orchestration

| Project | Role |
|---|---|
| `Viv.Aspire.AppHost` | .NET Aspire orchestrator — launches all services |
| `Viv.Aspire.Gateway` | **YARP** reverse proxy with rate limiting, output cache, JWT auth token forwarding |
| `Viv.Aspire.ServiceDefaults` | OpenTelemetry, `/health` + `/alive` endpoints, service discovery, resilience |

## Key patterns

### Configuration: `viv.config.json`

Every API project has a `viv.config.json` at its root. `VivEngine.LoadVivConfig()` deserializes it into `VivOptions`, which contains sub-sections for logging, caching (Redis/memory), database (PostgreSQL/SQL Server with read-write split), messaging (RabbitMQ), and JWT.

### DI: Autofac + MS DI hybrid

`builder.Services.AddViv(vivOptions)` registers all framework services. Business-layer services and repositories are registered via **type scanning** driven by `DIOptions` — you specify assembly name, namespace, and class name suffix (e.g., `"ClassNameEndWith": "Service"`). `VivLocator` provides static access to both containers for scenarios where constructor injection isn't available.

### Database: EF Core + Dapper hybrid

`VivDatabaseContext` (implements `IVivDbContext`) uses EF Core for small batches and Dapper for large ones (configurable via `EFMaxCount`). `EFAppContext` pins to read or write at construction time — reads randomly select a slave connection, writes always use the master. Both PostgreSQL and SQL Server are supported.

### Multi-tenancy via headers

`VivContextMiddleware` reads `Viv_AppId`, `Viv_TenantId`, `Viv_UserId` from HTTP headers and sets them on `IVivContext` (scoped, backed by `AsyncLocal<long>`). Database operations use `TenantId` for logical isolation.

### Unified API response

Controllers return `VivApiResult` (implements `IActionResult`) — a `{ Code, Message, Data }` envelope with consistent JSON formatting.

### Messaging

`IVivProducer.PublishAsync<T>()` and `PublishDelayAsync<T>(TimeSpan)` wrap MassTransit. Messages must extend `VivMessage`. Consumers extend `VivConsumer<T>`, overriding `ReceiveMessageAsync` — return `SubscribeResult` to control success/requeue.
