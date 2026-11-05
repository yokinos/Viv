<div align="center">

<p align="center">
  <img src="assets/vivian.jpg" alt="薇薇安·班希" width="120" height="120" style="border-radius:50%" />
</p>

# Viv

**基于 .NET 10 的微服务基础设施框架** — 数据访问、消息、缓存、长连接、网关、可观测性一次装配。

业务侧只写 **实体 + Service + Repository + Handler**，框架负责扫描注册、租户隔离、统一响应、事务与投递语义。

<p align="center">
  <img src="https://img.shields.io/github/actions/workflow/status/yokinos/Viv/dotnet.yml?style=flat&logo=githubactions&logoColor=white&label=CI" />
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet" />
  <img src="https://img.shields.io/badge/C%23-14.0-239120?style=flat&logo=csharp" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=flat&logo=postgresql" />
  <img src="https://img.shields.io/badge/SQL_Server-2022-CC2927?style=flat&logo=microsoftsqlserver" />
  <img src="https://img.shields.io/badge/Redis-7.x-DC382D?style=flat&logo=redis" />
  <img src="https://img.shields.io/badge/RabbitMQ-3.x-FF6600?style=flat&logo=rabbitmq" />
  <img src="https://img.shields.io/badge/Aspire-13.x-8250DF?style=flat&logo=dotnet" />
</p>

```
   ██╗   ██╗██╗██╗   ██╗
   ██║   ██║██║██║   ██║
   ██║   ██║██║██║   ██║
   ╚██╗ ██╔╝██║╚██╗ ██╔╝
    ╚████╔╝ ██║ ╚████╔╝
     ╚═══╝  ╚═╝  ╚═══╝
```

</div>

## 核心能力

- **多租户隔离** — `Org > Company > Tenant` 分层；`IVivContext`（AsyncLocal）承载身份，EF 全局过滤、Dapper 自动追加租户条件
- **数据访问** — EF Core + Dapper 混合；读写分离、软删除、事务、按实体同步表结构
- **消息** — Wolverine + RabbitMQ 发布订阅（fanout，每个服务一条独立队列）、消费锁、延迟重投、死信
- **可靠投递** — Outbox 与业务写入同事务；Inbox 按消息级/业务级两族键去重
- **异步任务** — Skirk：长任务持久化、认领执行、进度上报、协作式取消、幂等入队
- **缓存与锁** — Redis 多库路由、Cache-Aside、分布式锁（可重入 + 自动续期）
- **长连接** — Kinna：连接表、租约、Ack/Resync 投递水位、单客户端单连接、上行限流、健康检查
- **网关** — YARP 反向代理；路由自动生成、短名可配置；JWT 解析 + `x-request-token` HMAC 验签
- **跨服务通信** — HTTP + gRPC，服务发现，`x-viv-*` 上下文透传
- **可观测性** — `/health` + `/alive`、`Viv.*` Meter、`IVivMeter` 进程内读数、db/redis/mq 链路 span、慢查询日志
- **工程化** — 中央包管理、Aspire 编排、OpenTelemetry + Seq、TickerQ 调度、源生成器、CLI

---

## 目录

- [快速开始](#快速开始)
- [配置与凭据](#配置与凭据)
- [架构](#架构)
- [框架用法](#框架用法)
- [测试与 CI](#测试与-ci)
- [约定与文档](#约定与文档)

---

## 快速开始

### 环境依赖

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 16+ 或 SQL Server 2022+
- Redis 7.x、RabbitMQ 3.x（不用的子系统可以不配，对应节点写 null）

### 构建与启动

```bash
# 构建全量（诊断用：-m:1 -nodeReuse:false 可避开并发编译的文件锁问题）
dotnet build Viv.slnx

# 一键启动全部服务（Aspire 编排，含网关）
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost

# 或单独启动
dotnet run --project src/Vivian/Viv.Apex.Api
dotnet run --project src/Vivian/Viv.Apex.Worker
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.Gateway

# 全量测试
dotnet test Viv.slnx
```

### 中央包管理

所有 NuGet 版本集中在根目录 [Directory.Packages.props](Directory.Packages.props)，各项目 `<PackageReference>` 不写 `Version`；已开启 `CentralPackageTransitivePinningEnabled`。

---

## 配置与凭据

### 凭据不入库

已入库的 `appsettings.json` 里，凭据类字段一律是空串：

```
TokenOption.SecretKey、EnvOption.InternalToken、S3Option.AccessKey/SecretKey、
LogOption.SeqApiKey、CacheOption.RedisOptions.ConnectionString/Password、
DatabaseOption.MasterConnectionString、NanaOption.Password/SagaConnectionString
```

三处来源按环境补：

1. **本地开发** — `appsettings.Development.json`（`.gitignore` 已排除，整份覆盖）。新机器 clone 后没有这个文件，需要自备。
2. **生产** — 环境变量，层级用双下划线：`VivOptions__TokenOption__SecretKey`、`VivOptions__EnvOption__InternalToken`、`VivOptions__DatabaseOption__MasterConnectionString`。
3. **不要写回 `appsettings.json`** —— 那份是共享且会提交的。

缺失时是**响亮的失败**：`TokenOption.SecretKey` 为空会让 JwtBearer 注册直接抛 `IDX10720`；连接串为空会在首次使用时报错。

### 宿主标识

`EnvOption` 里决定"我是谁、显示成什么、挂在网关哪个前缀下"：

```jsonc
"EnvOption": {
  "Env": 0,
  "ServiceName": "viv.apex.api",     // 服务标识（x-viv-serviceName）
  "DisplayName": "Viv Apex API",     // OpenAPI / Scalar 标题，缺省回落 ServiceName
  "ShortName": "apex",               // Scalar 里的网关前缀，缺省不设（等同直连服务）
  "MachineId": 2,
  "InternalToken": "",
  "TrustedProxies": ["172.16.0.0/12"] // 反向代理 / 网关网段；非 Development 不配就只信任 loopback
}
```

> **`Env` 不是宿主环境。** CORS、`X-Forwarded-*` 的信任判定只看 `ASPNETCORE_ENVIRONMENT`（`IHostEnvironment`），
> 不看这里的 `Env` —— 入库的 `appsettings.json` 写的是 `0`(Development)，生产容器照搬配置不会因此放开跨域或信任任意转发头。
> 未签名内部头的逃生开关（`AllowUnsignedInternalTrust`）现在要求**两个条件同时成立**：
> `EnvOption.Env` 与宿主环境都是 Development，所以「照搬配置 + 带开关」在生产会直接启动失败，而不是静默放行。
>
> `Env` 仍参与内部信任、密钥校验等业务分档，枚举值是 `0=Development / 1=Test / 2=PreRelease / 3=Production`。
> **生产必须用环境变量覆盖成 `3`**（`VivOptions__EnvOption__Env=3`），并同时设 `ASPNETCORE_ENVIRONMENT=Production`。
>
> **容器 / 反向代理部署**：非 Development 下未配 `TrustedProxies` 时只信任 loopback，Docker 网段来的
> `X-Forwarded-*` 会被丢弃，客户端 IP 退化成代理 IP（限流按代理 IP 分区 = 所有人共用一个桶）。
> 网关与服务都要配，例如 `VivOptions__EnvOption__TrustedProxies__0=172.16.0.0/12`；
> 网段按实际 `docker network inspect` 结果填，配错只会让转发头失效，不会放开信任。

> `ShortName` 只影响本服务自己的文档展示，**不参与网关路由**。网关的短名在网关自己的 `VivGateway:ShortNames` 里配（见 [网关](#网关)）。

---

## 架构

### 总览

```
                  ┌─────────────────────┐
                  │  Viv.Aspire.AppHost  │   Aspire 统一编排
                  └──────────┬──────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Viv.Aspire.Gateway  │   YARP + 限流 + 输出缓存 + 上下文头透传
                  └──────────┬──────────┘
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                    │
  ┌─────▼─────┐      ┌───────▼──────┐    ┌────────▼──────┐
  │ Apex.Api  │      │ DeepRed.Api  │    │ Herta.Api     │   Vivian 应用层
  │Apex.Worker│      │D.R.Worker    │    │ Herta.Link    │
  │Coser.Api  │      │Ouroboros     │    │ (SignalR)     │
  └─────┬─────┘      └───────┬──────┘    └────────┬──────┘
        │                    │                    │
        └────────────────────┼────────────────────┘
                             │
     ┌───────────┬───────────┼───────────┬────────────┐
     │           │           │           │            │
 ┌───▼───┐ ┌─────▼─────┐ ┌───▼───┐ ┌─────▼─────┐ ┌────▼────┐
 │ Momo  │ │   Nana    │ │ Redis │ │  Outbox   │ │  Kinna  │  Banshee 框架层
 │EF+Dao │ │Wolverine  │ │缓存/锁│ │ + Inbox   │ │ 长连接  │
 │多租户 │ │+RabbitMQ  │ │       │ │  + Skirk  │ │         │
 └───────┘ └───────────┘ └───────┘ └───────────┘ └─────────┘
```

### 请求链路

```
客户端携带 JWT
 → 网关：剥离客户端伪造的 x-viv-* 头与 query 身份参数 → JwtBearer 验签
 → 从 claims 回填 x-viv-*（含 holder-id），HMAC-SHA256 签名写 x-request-token（≤300s）
 → 下游：VivContextMiddleware 验签通过才信任头 → IVivContext（AsyncLocal）
 → 数据层：EF 全局租户过滤 / Dapper 自动追加 AND TenantId = @TenantId
 → 返回：{ Code, Message, Data }（HTTP 恒 200）
```

上下文头：`x-viv-appId`、`x-viv-subjectId`（= TenantId）、`x-viv-userId`、`x-viv-serviceName`、`x-viv-holder-id`、`x-request-token`。
签名密钥是 `EnvOption.InternalToken`（网关与所有服务同值，不回落 `TokenOption.SecretKey`）。
SignalR/WebSocket 升级带不了 Authorization 头，用 `?access_token=<JWT>`。

### 项目结构

```
Viv/
├── src/
│   ├── Banshee/                              # 框架层
│   │   ├── Viv.Contracts/                    # 基础契约（IVivContext、IDependency、IDistributedLock…）
│   │   ├── Viv.Delusion/                     # 工具：类型扫描、SafeMapper、加密、扩展
│   │   ├── Viv.Aoi/                          # DI 桥接（VivLocator）+ 内置容器 Paradox
│   │   ├── Viv.Engine/                       # 核心引擎：配置、AddViv、启动扩展、网关路由、自检
│   │   ├── Viv.Log/                          # Serilog / No-op + Seq
│   │   ├── Viv.Momo/                         # EF Core + Dapper 混合、读写分离、多租户、建表
│   │   ├── Viv.Nana/                         # Wolverine + RabbitMQ 发布订阅、Saga、MQTT
│   │   ├── Viv.Outbox/                       # Outbox（事务性投递）+ Inbox（消费幂等）
│   │   ├── Viv.Skirk/                        # 异步任务：入队、认领执行、进度、取消
│   │   ├── Viv.Redis/                        # 缓存 + 分布式锁（自动续期）
│   │   ├── Viv.Sandrone/                     # JWT、S3、二维码
│   │   ├── Viv.Echo/                         # HTTP + gRPC 客户端/服务端、服务发现
│   │   ├── Viv.Kinna/                        # 长连接推送框架（连接表、Ack/Resync、限流）
│   │   ├── Viv.Clockwork/                    # TickerQ 调度
│   │   ├── Viv.Cli/                          # REPL + 命令自动发现
│   │   └── Viv.Forge/                        # 源生成器基类
│   │
│   ├── Vivian/                               # 应用层
│   │   ├── Viv.Entity/                       # 数据库实体（按领域分目录）
│   │   ├── Viv.Elysia/                       # 请求校验管线 + 操作日志
│   │   ├── Viv.EventContracts/               # 跨服务事件定义
│   │   ├── Viv.Apex.Proxy/                   # 业务侧 gRPC 代理层（Ports + Contracts）
│   │   ├── Viv.Generators/、Viv.Meta/        # 应用源生成器与宿主
│   │   ├── Viv.Apex.Core|Api|Worker/         # Apex 领域
│   │   ├── Viv.DeepRed.Core|Entity|Api|Worker/
│   │   ├── Viv.Herta.Core|Api|Link/          # Herta（Link = SignalR）
│   │   ├── Viv.Ouroboros.Core|Api|Worker/
│   │   ├── Viv.Coser.Api/                    # Coser（TickerQ）
│   │   ├── Viv.Toolbox/                      # 控制台工具箱（VivAppBuilder 实例）
│   │   └── Viv.Aspire/
│   │       ├── Viv.Aspire.AppHost/
│   │       ├── Viv.Aspire.Gateway/
│   │       └── Viv.Aspire.ServiceDefaults/
│   │
│   └── Test/                                 # 框架各项目单测 + Viv.Fakes（统一替身）
```

---

## 框架用法

### 一行启动：API / Worker / Gateway

```csharp
// ── API ──────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivApi(mvc => mvc.Filters.Add<RequestFilterAttribute>());
builder.RunVivApi(app => app.MapDefaultEndpoints());

// ── Worker（消息消费者 / 后台任务）──────────────────
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddVivWorker();
builder.Services.AddHostedService<Worker>();
builder.RunVivWorker();

// ── Gateway（YARP）───────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivGateway(ignoreSslErrors: builder.Environment.IsDevelopment());
builder.RunVivGateway(app => app.MapDefaultEndpoints());
```

`AddVivApi` 依次完成：绑定 `VivOptions` → Autofac 容器 → `AddViv()` → JWT → MVC/JSON/CORS/OpenAPI → 编码注册。
标题与网关前缀取自 `EnvOption.DisplayName` / `ShortName`，宿主代码不再传名字。
`RunVivApi` 的 `configure` 回调可追加端点（`app.UseTickerQ()`、`app.MapHub<ChatHub>("/chat")` 等）。

### `VivOptions` 各段

| 段 | 作用 | 备注 |
|---|---|---|
| `EnvOption` | 环境、服务标识、`InternalToken`、`DisplayName`/`ShortName` | `InternalToken` 网关与服务必须同值 |
| `DIOption` | Service / Repository 扫描规则 | 与 `IDependency` 标记共同构成注册来源 |
| `CacheOption` | Redis 连接、模式、多库分配 | 支持 Standalone / Cluster / Sentinel |
| `LogOption` | Serilog、Seq | |
| `DatabaseOption` | 方言、读写分离、实体扫描、慢查询阈值 | |
| `NanaOption` | RabbitMQ、消费类型、重试、Saga | |
| `OutboxOption` | 发件箱投递器、保留期 | 为 null = 不启用 |
| `InboxOption` | Inbox 清理保留期 | 与别段不同：为 null 不是"不启用" |
| `MqttOption` | MQTT 连接 | |
| `TokenOption` | JWT 密钥、有效期、签发方 | |
| `TickOption` | TickerQ 调度 | |
| `EchoOption` | HTTP / gRPC 服务端 | `GrpcOption.Port` 需要独立端口 |
| `CorsOption` | 允许的 Origin | 未配置时：Development 回显任意 Origin（局域网访问 dev server 需要），其余环境不放行任何跨域来源 |
| `S3Option` | 对象存储 | |
| `SkirkOption` | 异步任务（`EnableWorker` 决定本宿主跑不跑） | |

### DI 注册与启动自检

两条注册路径并存：

- **`DIOption` 扫描** —— 按程序集 + 命名空间 + 类名后缀（`Service` / `Repository`）注册为 Scoped
- **`IDependency` 标记** —— 实现类自己声明，可配 `[VivDependency(Lifetime = ..., AsSelf = ..., tag)]` 控制生命周期、自注册、keyed 注册（如按用户类型、客户端技术栈取不同实现）

启动时会做一次**漏注册自检**：名字以 `Service` / `Repository` 结尾、却既没实现 `IDependency` 也不在扫描范围内的类，直接抛异常并点名 —— 否则要等到某个接口解析失败才发现，现场看不出是谁漏了。测试项目与 `Viv.Fakes` 不参与自检。

### 数据库 Momo

```csharp
public class UserService : IUserService
{
    private readonly IMomoDbContext _db;
    public UserService(IMomoDbContext db) => _db = db;

    public async Task<VivApiResult> GetByIdAsync(long id)
    {
        var user = await _db.FindAsync<User>(id);       // 自动租户隔离
        return user is not null
            ? VivApiResult.Success(user)
            : VivApiResult.Failed("用户不存在");
    }
}
```

内置：读写分离、`ITenant` 租户过滤、`ISoftDeleted` 软删除、事务三件套、`SyncTableAsync` 按实体同步表。

> [!IMPORTANT]
> `SchemaSynchronizer` 只建/改表，**不生成索引**。唯一索引要手工 DDL；`SyncTableAsync` 也不在启动路径上（Toolbox 的 `initviv` 命令才会触发）。

### 消息：Nana / Outbox / Skirk

**发布订阅**：事件进 fanout 交换机 `{EventName}Exchange`，每个订阅服务一条独立队列各收一份；同服务只处理一次由消费锁 `nana:{ServiceName}:{EventType}:{MessageId}` 保证。

```csharp
// 事务性投递：与业务写入同一个本地事务，提交后才可能被投递
await _outbox.EnqueueAsync(new SendMessageEvent { /* ... */ });

// 消费
public class UserCreatedConsumer : VivConsumer<UserCreated>
{
    public UserCreatedConsumer(VivConsumerDependency dependency) : base(dependency) { }

    public override Task<SubscribeResult> ReceiveMessageAsync(
        NanaEnvelope<UserCreated> message, CancellationToken ct = default)
        => Task.FromResult(SubscribeResult.Success());
}
```

**异步任务（Skirk）**：长任务持久化到 `VivAsyncTask`，Worker 原子认领后执行，支持进度上报与协作式取消。

```csharp
var taskId = await _tasks.EnqueueAsync("export-users", new ExportParams(tenantId), idempotencyKey: $"{userId}:export:{tenantId}");
// 处理器
public class ExportUsersHandler : VivAsyncTaskHandler<ExportParams>
{
    public override string TaskType => "export-users";
    protected override async Task ExecuteAsync(ExportParams? request, IVivAsyncTaskContext context, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            context.ThrowIfCancelRequested();
            await context.ReportAsync(i + 1, 100, "导出中");
        }
    }
}
```

### 缓存与分布式锁

```csharp
await _redis.AddAsync("key", value, TimeSpan.FromMinutes(5));
await _lock.AcquireLockWithExecuteAsync("biz:order:42", TimeSpan.FromSeconds(30), async () =>
{
    /* 临界区 */
    return true;
});
```

Redis 故障时读路径回源数据库、取锁走回源兜底；写路径仍抛 `VivConnectionException`（映射 `-502`）。

### 跨服务通信：Echo

- **HTTP**：`AddVivGrpcClient` 同源的客户端封装，透传 `x-viv-*` 并签名
- **gRPC**：`[BindServiceMethod]` 实现自动发现并映射；客户端 `GrpcChannel` 走 HTTP/2
- **端口约束**：明文下 `Http1AndHttp2` 只认 TLS/ALPN，因此 **REST 与 gRPC 必须分端口** —— gRPC 端口严格 HTTP/2，REST 端口强制 HTTP/1.1。浏览器/curl 直接访问 gRPC 端口会得到 `An HTTP/1.x request was sent to an HTTP/2 only endpoint`

### 长连接：Kinna

一个 Hub 一套运行实例，进程内连接表按 `(SubjectId, UserId)` 与 `(AppId, SubjectId, UserId)` 双索引：

```csharp
builder.Services.AddKinna<ChatHub>();
builder.Services.AddKinnaPusher<ChatHub, SendMessageEvent, ChatMessagePusher>();
// ...
app.MapKinnaHub<ChatHub>("/chat");
```

- **Ack / Resync**：客户端确认已收到的消息，重连时拿水位判断是否补拉；存储走 `IKinnaDeliveryStore<THub>` 端口，业务侧决定落库还是落缓存
- **单客户端单连接**：按 AppId 判，新连接登记时摘掉同租户同用户的旧连接（本副本内）
- **健康检查 / 上行限流**：`Ping`/`Ack` 按连接限流，被挡的 Ping 回 Pong 但不续租

### 可观测性

- **健康检查** — `/health`（全检查）、`/alive`（只判进程）
- **指标** — Meter `Viv.Momo` / `Viv.Redis` / `Viv.Nana` / `Viv.Outbox` / `Viv.Kinna`；`IVivMeter` 提供进程内读数（给管理后台用）
- **链路追踪** — `ActivitySource: Viv`：`db.query`、`redis.command`、`mq.publish`；消费侧用 Wolverine 自带源
- **慢查询** — `SlowQueryThresholdMs`（默认 1000，0 = 关），超阈值记 Warning

### 统一响应

```json
{ "code": 200, "message": "请求处理成功", "data": { } }
```

业务成败看 `Code`，与 HTTP 状态码无关（业务失败也是 HTTP 200）。区间：`2xx` 成功、`-2xx` 参数/业务、`-4xx` 身份、`-5xx` 系统（`-501` 数据库 / `-502` 缓存 / `-503` 消息队列）、`-6xx` 权限。

### 网关

路由从 Aspire 服务发现自动生成，零手写 JSON：

```
/{短名}/api/{**catch-all}             →  /api/{**catch-all}       标准 API（如 /apex/api/...）
/docs/{短名}/scalar/{**catch-all}     →  /scalar/{**catch-all}    Scalar 文档
/docs/{短名}/openapi/{**catch-all}    →  /openapi/{**catch-all}   OpenAPI 文档
/ws/{短名}/{**catch-all}              →  /{**catch-all}          WebSocket / SignalR
```

文档路由只放 `/scalar/**` 与 `/openapi/**` 两段：早期是全通配 `/docs/{短名}/**`，等于给下游任意路径
（含 `/api/**`）开了一条不经过限流的旁路。

`/ws` 路由保留全通配，是因为 Hub 的挂载路径由各服务自己决定（`MapKinnaHub<THub>("/chat")`），网关不掌握清单。
代价是 `/ws/{短名}/api/xxx` 在 HTTP 语义上也能打到下游 `/api/xxx`，所以这条路由**必须**依赖下游自身的
`[Authorize]`；网关侧的保障是：JWT 认证（`access_token` 查询参数只允许出现在 `/ws` 前缀下）+ 与 api 路由相同的限流策略。

**短名优先取网关配置**，没配才按服务名推断（`split('-')` 第二段；冲突时 `-api` 保留基础名、其余拼接剩余段）：

```jsonc
// Viv.Aspire.Gateway/appsettings.json
"VivGateway": {
  "ShortNames": {
    "viv-apex-api": "apex",
    "viv-herta-link": "hertalink"
  }
}
```

短名会拼进路由模板，因此启动时校验：只允许小写字母/数字/连字符，两个服务撞同一短名直接抛（报错点名双方），配成空串也报错。

网关只解析 JWT 并回填上下文头，**不强制鉴权**；鉴权由下游 `[Authorize]` 自己控制。
限流策略在 `viv.ratelimit.json` 里配，按**客户端 IP 分区**计数（不是全网关共用一个桶），改文件后下一次请求即生效。
`PermitLimit <= 0` 表示不限流；超限返回 429。

### CLI

```csharp
[VivCommand("migrate", "执行数据库迁移")]
public class Cmd_Migrate : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext ctx) { /* ... */ }
}
```

`InputMagic.GetInput(...)` / `Out.PrintlnFormatJson(obj)` 提供交互与格式化输出。

---

## 测试与 CI

- `src/Test/` 每个框架项目一套单测；业务测试项目在各自目录旁（`Viv.Apex.Tests`、`Viv.Herta.Tests` 等）
- `src/Test/Viv.Fakes/` 是全仓**唯一**允许手写测试替身的地方，各测试项目引用它
- CI：GitHub Actions [.github/workflows/dotnet.yml](.github/workflows/dotnet.yml) 跑全量测试并上报覆盖率

```bash
dotnet test Viv.slnx
```

---

## 约定与文档

- 更细的架构决策、踩坑记录与硬约定集中在 [CLAUDE.md](CLAUDE.md)：事件契约必须 public setter、群消息读写都校验成员、会话行并发与唯一索引 DDL、投递水位落库、网关短名来源、凭据存放位置等
- 数据库方言差异统一走 `SqlMagic`（表名/列名/分页），手写 SQL 不要写死物理表名
- 新增业务项目：`Core`（实体模型/接口/服务/仓储）+ `Api`（控制器入口）+ 可选 `Worker`（消费者），配置里补一段 `VivOptions`
