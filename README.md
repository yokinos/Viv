<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet" />
  <img src="https://img.shields.io/badge/C%23-13.0-239120?style=flat&logo=csharp" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=flat&logo=postgresql" />
  <img src="https://img.shields.io/badge/SQL_Server-2022-CC2927?style=flat&logo=microsoftsqlserver" />
  <img src="https://img.shields.io/badge/Redis-7.x-DC382D?style=flat&logo=redis" />
  <img src="https://img.shields.io/badge/RabbitMQ-3.x-FF6600?style=flat&logo=rabbitmq" />
  <img src="https://img.shields.io/badge/Aspire-13.x-8250DF?style=flat&logo=dotnet" />
</p>

# Viv

基于 **.NET 10** 的微服务基础设施框架。从数据访问、消息队列、缓存、认证到网关编排，提供一站式解决方案。业务开发只需关注 **Service + Repository**，基础设施由框架统一处理。

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

| 层级 | 代号 | 定位 |
|:--|:--|:--|
| **Banshee** | 报丧女妖 | 框架层 — 幕后驱动一切基础设施 |
| **Vivian** | 薇薇安 | 应用层 — 台前承载业务逻辑 |

---

## 架构

```
                  ┌─────────────────────┐
                  │  Viv.Aspire.AppHost  │  Aspire 统一编排
                  └──────────┬──────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Viv.Aspire.Gateway  │  YARP 反向代理 + 限流 + 缓存
                  └──────────┬──────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
   ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
   │ Apex.Api  │  │DeepRed.Api│  │Herta.Api  │  ...  Vivian 服务
   │Apex.Worker│  │D.R.Worker │  │Herta.Link │
   └─────┬─────┘  └─────┬─────┘  └─────┬─────┘
         │              │              │
         └──────────────┼──────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
  ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
  │   Momo    │  │   Nana    │  │   Redis   │  Banshee 基础设施
  │ EF+Dapper │  │MassTransit│  │  缓存/锁   │
  │ 读写分离   │  │+RabbitMQ  │  │           │
  └───────────┘  └───────────┘  └───────────┘
```

### 请求管线

```
Request
  → NotFoundMiddleware         404 拦截
  → VivContextMiddleware        解析 Header → IVivContext（多租户）
  → RequestFilterAttribute     请求校验（Elysia）
  → VivExceptionFilter         全局异常捕获
  → Controller Action          业务处理
  → VivApiResult               统一响应 { Code, Message, Data }
```

---

## 项目结构

```
Viv/
├── src/
│   ├── Banshee/                          # 框架层
│   │   ├── Viv.Contracts/                # 基础接口与枚举
│   │   ├── Viv.Delusion/                 # 通用工具 — 类型扫描、对象映射、加密
│   │   ├── Viv.Aoi/                      # DI 桥接 — MS DI ↔ Autofac
│   │   ├── Viv.Engine/                   # 核心引擎 — 配置加载、统一注册、启动扩展
│   │   ├── Viv.Cli/                      # CLI 框架 — REPL + 命令自动发现
│   │   ├── Viv.Log/                      # 日志 — Serilog / Seq
│   │   ├── Viv.Momo/                     # 数据库 — EF Core + Dapper 混合
│   │   ├── Viv.Nana/                     # 消息队列 — MassTransit + RabbitMQ
│   │   ├── Viv.Redis/                    # 缓存 — StackExchange.Redis
│   │   ├── Viv.Authentication/           # 认证 — JWT
│   │   ├── Viv.Echo/                     # 跨服务通信 — HTTP + gRPC
│   │   ├── Viv.Tick/                     # 后台调度 — TickerQ
│   │   └── Viv.Forge/                    # 代码生成
│   │
│   ├── Vivian/                           # 应用层
│   │   ├── Viv.Entity/                   # 数据库实体
│   │   ├── Viv.Elysia/                   # 请求校验管线
│   │   ├── Viv.EventContracts/           # 跨服务消息定义
│   │   ├── Viv.Sdk/                      # 公共 SDK
│   │   │
│   │   ├── Viv.Apex.Core/                # Apex 业务核心
│   │   ├── Viv.Apex.Api/                 # Apex API 服务
│   │   ├── Viv.Apex.Worker/              # Apex 消息消费者
│   │   │
│   │   ├── Viv.DeepRed.Core/             # DeepRed 业务核心
│   │   ├── Viv.DeepRed.Api/              # DeepRed API 服务
│   │   ├── Viv.DeepRed.Worker/           # DeepRed 消息消费者
│   │   │
│   │   ├── Viv.Herta.Core/               # Herta 业务核心
│   │   ├── Viv.Herta.Api/                # Herta API 服务
│   │   ├── Viv.Herta.Link/               # Herta SignalR 实时通讯
│   │   │
│   │   ├── Viv.SakuMai.Api/              # SakuMai API（TickerQ 集成）
│   │   │
│   │   └── Viv.Aspire/
│   │       ├── Viv.Aspire.AppHost/        # 统一编排启动
│   │       ├── Viv.Aspire.Gateway/        # YARP 反向代理
│   │       └── Viv.Aspire.ServiceDefaults/ # OpenTelemetry + 健康检查
│   │
│   └── Test/
│       └── Viv.Test/                     # CLI 命令集
```

---

## 快速开始

### 环境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 16+ 或 SQL Server 2022+
- Redis 7.x
- RabbitMQ 3.x

### 启动

```bash
git clone <repo-url>
cd Viv

# 一键启动全部服务
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost

# 或单独启动
dotnet run --project src/Vivian/Viv.Apex.Api
dotnet run --project src/Vivian/Viv.Apex.Worker
dotnet run --project src/Vivian/Viv.Herta.Link
```

### 添加新服务 — 三行代码起步

```csharp
// ── API ──
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddVivApi("Viv NewApi API", mvc => mvc.Filters.Add<RequestFilterAttribute>());
builder.RunVivApi(app => app.MapDefaultEndpoints());

// ── Worker ──
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddVivWorker();
builder.Services.AddHostedService<Worker>();
builder.RunVivWorker();
```

`AddVivApi` / `AddVivWorker` 自动完成：配置加载 → Autofac 容器 → MVC/Swagger/CORS → 中间件管线。`viv.config.json` 放到项目根目录即可。

---

## 配置：`viv.config.json`

```jsonc
{
  "Env": 0,                              // 0=Dev 1=Test 2=PreRelease 3=Production
  "DIOption": {
    "ServiceImplementation": {           // Service 自动扫描注册
      "AssemblyName": "Viv.Apex.Core",
      "NameSpace": "Viv.Apex.Core.Service",
      "ClassNameEndWith": "Service"
    },
    "RepositoryImplementation": {        // Repository 自动扫描注册
      "AssemblyName": "Viv.Apex.Core",
      "NameSpace": "Viv.Apex.Core.Repository",
      "ClassNameEndWith": "Repository"
    }
  },
  "DatabaseOption": {
    "DatabaseSource": 0,                 // 0=SqlServer 1=PostgreSQL
    "IsReadWriteSplit": false,
    "MasterConnectionString": "Server=...;Database=viv;...",
    "SlaveConnectionStrings": [],
    "EntityTypeOptions": [{              // 实体自动扫描
      "AssemblyName": "Viv.Entity",
      "NameSpace": "Viv.Entity.Database.Apex",
      "BaseType": "Viv.Momo.Interface.IEntity"
    }]
  },
  "NanaOption": {
    "Host": "localhost", "Port": 5672,
    "UserName": "viv", "Password": "***",
    "VirtualHost": "/Viv", "RetryCount": 3,
    "ConsumerTypes": []                  // 消费者类型扫描规则
  },
  "CacheOption": {
    "CacheProviderType": 1,              // 0=None 1=Redis
    "IsEnableMemoryCache": true,
    "RedisOptions": { "ConnectionString": "localhost:6379" }
  },
  "LogOption": { "LogType": 1 },        // 0=None 1=Serilog
  "TokenOption": { "TokenType": 0, "SecretKey": "***", "ExpireMinutes": 120 },
  "EchoOption": { "EnableHttp": true, "EnableGrpc": true },
  "TickOption": { ... }                 // TickerQ 调度配置
}
```

---

## 编写业务代码

### 实体

```csharp
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex;

public class User : IEntity
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### Service（自动注册到 DI）

```csharp
namespace Viv.Apex.Core.Service;

public class UserService : IUserService
{
    private readonly IVivDbContext _db;

    public UserService(IVivDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(long id)
        => await _db.FindAsync<User>(id);

    public async Task<List<User>> SearchAsync(string keyword)
        => await _db.FindListAsync<User>(u => u.Name.Contains(keyword));
}
```

### Controller

```csharp
[ApiController, Route("api/[controller]")]
public class UserController(UserService svc) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<VivApiResult> Get(long id)
    {
        var user = await svc.GetByIdAsync(id);
        return user is not null ? VivApiResult.Success(data: user)
                                : VivApiResult.Error("用户不存在");
    }
}
```

### 消息生产者

```csharp
public class OrderService(IVivPublisher producer)
{
    public async Task PlaceAsync()
    {
        await producer.PublishAsync(new OrderCreated { OrderId = 1 });
        await producer.PublishDelayAsync(TimeSpan.FromMinutes(30),
            new CheckPayment { OrderId = 1 });
    }
}
```

### 消息消费者

```csharp
public class OrderCreatedConsumer : VivConsumer<OrderCreated>
{
    public override async Task<SubscribeResult> ReceiveMessageAsync(
        NanaMessage<OrderCreated> message, CancellationToken ct)
    {
        // 处理业务...
        return SubscribeResult.Success();
    }
}
```

---

## Viv.Cli — 命令行工具框架

引 `Viv.Cli`，写 `[VivCommand]`，自动扫描注册：

```csharp
[VivCommand("migrate", "执行数据库迁移")]           // 单命令
[VivCommand("clear, cl", "清除屏幕")]               // 多别名
public class Cmd_Migrate : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext ctx) { ... }
}
```

内置 `clear`（别名 `cl`）命令。交互输入和格式化输出：

```csharp
var name = InputMagic.GetInput("请输入名称");         // 必填
var pwd  = InputMagic.GetInput("密码", secret: true); // 隐藏
if (InputMagic.Confirm("确认?")) { ... }             // y/n

Out.PrintlnSuccess("完成");
Out.PrintlnFormatJson(someObject);                   // JSON Panel
```

---

## Momo 数据库

| 数据量 | 引擎 | 说明 |
|:--|:--|:--|
| < `EFMaxCount` | EF Core | 变更追踪 |
| ≥ `EFMaxCount` | Dapper | 纯 SQL 高吞吐 |

- **读写分离** — `EFAppContext` 构造时锁定读写方向
- **多租户** — 通过 `IVivContext.TenantId` 自动隔离
- **软删除** — 实现 `ISoftDelete` 即可

## Nana 消息队列

MassTransit + RabbitMQ，内置重试（默认 3 次）和 Saga 状态机支持。`SubscribeResult.Requeue()` 将失败消息退队重试。

## API 统一响应

```json
{ "code": 200, "message": "successful", "data": { ... } }
```

| code | 含义 |
|:--|:--|
| `200` | 成功 |
| `-200` | 通用业务错误 |
| `-400` | Token 无效 |
| `-404` | 资源不存在 |
| `-500` | 服务端异常 |

## 多租户

| Header | 说明 |
|:--|:--|
| `Viv_AppId` | 应用 ID |
| `Viv_TenantId` | 租户 ID |
| `Viv_UserId` | 用户 ID |

`VivContextMiddleware` 自动解析 Header 注入 `IVivContext`。

---

## License

MIT
