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

基于 **.NET 10** 的微服务基础设施框架，提供从数据访问、消息队列、缓存、认证到网关编排的一站式解决方案。目标是让业务开发只需关注 **Service + Repository**，其余由框架统一处理。

```
   ██╗   ██╗██╗██╗   ██╗
   ██║   ██║██║██║   ██║
   ██║   ██║██║██║   ██║
   ╚██╗ ██╔╝██║╚██╗ ██╔╝
    ╚████╔╝ ██║ ╚████╔╝
     ╚═══╝  ╚═╝  ╚═══╝
```

---

## 命名由来

| 层级 | 代号 | 含义 |
|:--|:--|:--|
| **Banshee** | 报丧女妖 | 框架层 — 女妖在幕后沉默地驱动一切 |
| **Vivian** | 薇薇安 | 应用层 — 站在台前与用户交互的存在 |

---

## 项目结构

```
Viv/
├── src/
│   ├── Banshee/                          # 框架层（不可单独运行）
│   │   ├── Viv.Contracts/                # 基础契约 — 根接口、枚举、异常定义
│   │   ├── Viv.Vva/                      # 通用工具库 — ID生成器、类型扫描、加密、对象映射
│   │   ├── Viv.Aoi/                      # 依赖注入桥接 — MS DI ↔ Autofac 服务定位器
│   │   ├── Viv.Engine/                   # 核心引擎 — 配置加载、统一注册、中间件、API响应封装
│   │   ├── Viv.Log/                      # 日志抽象 — Serilog / 空实现切换
│   │   ├── Viv.Momo/                     # 数据库 — EF Core + Dapper 混合，读写分离
│   │   ├── Viv.Nana/                     # 消息队列 — MassTransit + RabbitMQ，支持延迟消息
│   │   ├── Viv.Redis/                    # 缓存 — StackExchange.Redis，多模式多库分配
│   │   └── Viv.Authentication/           # 认证 — JWT 令牌签发与校验
│   │
│   ├── Vivian/                           # 应用层
│   │   ├── Viv.Entity/                   # 数据库实体定义
│   │   ├── Viv.Elysia/                   # 请求校验管线 — 自动验证、统一过滤
│   │   ├── Viv.Apex.Core/                # 核心业务 — Service + Repository 实现
│   │   ├── Viv.Apex.Api/                 # 主 API 服务入口
│   │   ├── Viv.Herta.Api/                # Herta API 服务
│   │   ├── Viv.Herta.Link/               # Herta Link 服务
│   │   ├── Viv.Robin.Api/                # Robin API 服务
│   │   ├── Viv.Toolkit/                  # CLI 工具集
│   │   ├── Viv.Sdk/                      # 公共 SDK
│   │   └── Viv.Aspire/                   # .NET Aspire 编排
│   │       ├── Viv.Aspire.AppHost/        #   AppHost — 统一启动所有服务
│   │       ├── Viv.Aspire.Gateway/        #   Gateway — YARP 反向代理 + 限流 + 缓存
│   │       └── Viv.Aspire.ServiceDefaults/ #  OpenTelemetry、健康检查、服务发现
│   │
│   └── Test/
│       └── Viv.Test/                     # 测试
```

---

## 架构总览

```
                    ┌─────────────┐
                    │   Client    │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
                    │   Gateway   │  ← YARP 反向代理
                    │ 限流/缓存/JWT│    请求头转发 (X-User-Id, Viv_AppId, Viv_TenantId...)
                    └──────┬──────┘
                           │
            ┌──────────────┼──────────────┐
            │              │              │
      ┌─────▼─────┐  ┌────▼────┐  ┌─────▼─────┐
      │ Apex.Api  │  │Herta.* │  │ Robin.Api │  ← Vivian 业务服务
      └─────┬─────┘  └────┬────┘  └─────┬─────┘
            │              │              │
            └──────────────┼──────────────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
        ┌─────▼────┐ ┌────▼────┐ ┌─────▼────┐
        │  Momo    │ │  Nana   │ │  Redis   │  ← Banshee 基础设施
        │ DB 读写分离│ │ RabbitMQ│ │ 缓存/锁  │
        └──────────┘ └─────────┘ └──────────┘
```

### 请求处理管线

```
HTTP Request
  → NotFoundMiddleware         (处理 404)
  → VivContextMiddleware       (解析 Header → IVivContext)
  → RequestFilterAttribute     (Elysia 请求校验)
  → VivExceptionFilter         (全局异常捕获 → VivApiResult)
  → Controller Action
  → VivApiResult               (统一响应 {Code, Message, Data})
```

---

## 快速开始

### 1. 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 16+ 或 SQL Server 2022+
- Redis 7.x
- RabbitMQ 3.x

### 2. 克隆并启动

```bash
git clone <your-repo-url>
cd Viv

# 通过 Aspire 一键启动全部服务
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost
```

> Aspire 会自动编排 Apex.Api + Herta.Api + Herta.Link + Robin.Api 的启动。Gateway 尚在联调中，可先以各 API 直连模式开发。

### 3. 配置文件

每个 API 项目的根目录下都有一个 `viv.config.json`，完整配置示例：

```jsonc
{
  "Env": 0,                     // 0=Dev, 1=Test, 2=PreRelease, 3=Production
  "DIOption": {
    "ServiceImplementation": {
      "AssemblyName": "Viv.Apex.Core",
      "NameSpace": "Viv.Apex.Core.Service",
      "ClassNameEndWith": "Service"       // 自动扫描以 Service 结尾的类
    },
    "RepositoryImplementation": {
      "AssemblyName": "Viv.Apex.Core",
      "NameSpace": "Viv.Apex.Core.Repository",
      "ClassNameEndWith": "Repository"
    }
  },
  "CacheOption": {
    "CacheProviderType": 1,              // 0=无, 1=Redis
    "IsEnableMemoryCache": true,         // 同时启用内存缓存
    "RedisOptions": {
      "RedisMode": 0,                    // 0=单机, 1=集群, 2=哨兵
      "ConnectionString": "localhost:6379,password=vivRedis"
    }
  },
  "LogOption": {
    "LogType": 1                         // 0=None, 1=Serilog
  },
  "DatabaseOption": {
    "DatabaseSouce": 1,                  // 0=SqlServer, 1=PostgreSQL
    "IsReadWriteSplit": true,
    "MasterConnectionString": "Server=localhost;Database=vivApex;...",
    "SlaveConnectionStrings": [
      "Server=localhost;Database=vivApexRead;..."
    ],
    "Timeout": 30,
    "EntityTypeOptions": [{              // 自动扫描数据库实体
      "AssemblyName": "Viv.Entity",
      "NameSpace": "Viv.Entity.Database",
      "BaseType": "Viv.Momo.Interface.IEntity"
    }]
  },
  "NanaOption": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "viv",
    "Password": "vivRabbitMQ",
    "VirtualHost": "/Viv",
    "RetryCount": 3,
    "ConsumerTypes": []                  // 消费者类型扫描规则
  },
  "TokenOption": {
    "TokenType": 0,                      // 0=JWT
    "SecretKey": "your-secret-key",
    "ExpireMinutes": 120
  }
}
```

### 4. 编写第一个业务

**定义实体：**

```csharp
using Viv.Momo.Interface;

namespace Viv.Entity.Database;

public class User : IEntity
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**编写 Service：**

```csharp
using Viv.Momo;
using Viv.Engine;
using Viv.Entity.Database;

namespace Viv.Apex.Core.Service;

public class UserService : IUserService  // 自动扫描注册到 DI
{
    private readonly IVivDbContext _db;

    public UserService(IVivDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(long id)
        => await _db.FindAsync<User>(id);

    public async Task<bool> CreateAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        return await _db.InsertAsync(user);
    }

    public async Task<List<User>> SearchAsync(string keyword)
        => await _db.FindListAsync<User>(u => u.Name.Contains(keyword));
}
```

**编写 Controller：**

```csharp
using Microsoft.AspNetCore.Mvc;
using Viv.Engine;
using Viv.Apex.Core.Service;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;

    public UserController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{id}")]
    public async Task<VivApiResult> Get(long id)
    {
        var user = await _userService.GetByIdAsync(id);
        return user is not null
            ? VivApiResult.Success(data: user)
            : VivApiResult.Error("用户不存在");
    }

    [HttpPost]
    public async Task<VivApiResult> Create(User user)
    {
        var ok = await _userService.CreateAsync(user);
        return ok
            ? VivApiResult.Success("创建成功")
            : VivApiResult.Error("创建失败");
    }
}
```

### 5. 发布消息

```csharp
public class OrderService
{
    private readonly IVivProducer _producer;

    public OrderService(IVivProducer producer)
    {
        _producer = producer;
    }

    public async Task PlaceOrderAsync()
    {
        // 即时消息
        await _producer.PublishAsync(new OrderCreatedMessage { OrderId = 1 });

        // 延迟消息（30分钟后检查支付状态）
        await _producer.PublishDelayAsync(
            TimeSpan.FromMinutes(30),
            new CheckPaymentMessage { OrderId = 1 }
        );
    }
}
```

**消费消息：**

```csharp
public class OrderCreatedConsumer : VivConsumer<OrderCreatedMessage>
{
    public override async Task<SubscribeResult> ReceiveMessageAsync(
        NanaMessage<OrderCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        // 处理业务...
        return SubscribeResult.Success();
    }
}
```

---

## Momo 数据库

Momo 采用 **EF Core + Dapper 混合**策略：

| 数据量 | 执行引擎 | 说明 |
|:--|:--|:--|
| < `EFMaxCount` | EF Core | 利用变更追踪，批量较小 |
| ≥ `EFMaxCount` | Dapper | 纯 SQL，高吞吐量 |

**读写分离**：`EFAppContext` 在构造时就锁定读库或写库，读操作随机选取从库，写操作始终走主库。

**软删除**：实现 `ISoftDelete` 接口即可。

**多租户**：通过 `IVivContext.TenantId` 隔离数据。

---

## Nana 消息队列

基于 MassTransit + RabbitMQ，提供：

- **即时消息** — `PublishAsync<T>()`
- **延迟消息** — `PublishDelayAsync<T>(TimeSpan)`
- **自动重试** — 默认 3 次，间隔 1 秒
- **失败重入队** — `SubscribeResult.Requeue()` 将消息退回队列

---

## 网关 Gateway 🚧

> 基于 YARP 反向代理，目前还在联调阶段，核心逻辑已搭建，待 YARP 配置调通。

| 功能 | 实现 | 状态 |
|:--|:--|:--|
| 反向代理 | YARP + 服务发现 | 🚧 联调中 |
| 限流 | 固定窗口算法，支持自定义策略 | ✅ |
| 输出缓存 | 按策略配置过期时间 | ✅ |
| 身份传递 | JWT 解析后通过 `X-User-Id` / `X-User-Name` Header 转发 | ✅ |
| 会话亲和性 | Session Affinity | 🚧 |
| 被动健康检查 | Passive Health Checks | 🚧 |

---

## API 统一响应

所有接口返回 `VivApiResult`：

```json
{
  "code": 200,
  "message": "successful",
  "data": { ... }
}
```

| 状态码 | 含义 |
|:--|:--|
| `200` | 成功 |
| `-200` | 通用业务错误 |
| `-400` | Token 无效 |
| `-401` | 认证失败 / 签名错误 |
| `-403` | 无权限 |
| `-404` | 资源不存在 |
| `-500` | 服务端异常 |

---

## 多租户

通过 HTTP Header 传递租户上下文：

| Header | 说明 |
|:--|:--|
| `Viv_AppId` | 应用 ID |
| `Viv_TenantId` | 租户 ID |
| `Viv_UserId` | 用户 ID |

`VivContextMiddleware` 自动将 Header 注入到 `IVivContext`，数据库操作基于 `TenantId` 隔离。

---

## 当前进度

- [x] Banshee 框架核心（DI、日志、配置）
- [x] Momo 数据库读写分离 + EF/Dapper 混合
- [x] Nana 消息队列（MassTransit + RabbitMQ）
- [x] Redis 缓存（单机 / 集群 / 哨兵）
- [x] JWT 认证
- [x] .NET Aspire 编排（AppHost + ServiceDefaults）
- [ ] YARP 网关 — 正在联调
- [ ] 业务模块（Apex / Herta / Robin 业务实现）
- [ ] **分布式 IM** — 基于 WebSocket 的实时通讯系统
  - [ ] WebSocket 长连接管理
  - [ ] 消息路由与广播
  - [ ] 离线消息存储
  - [ ] 已读/未读状态同步
  - [ ] 在线状态感知

---

## License

MIT
