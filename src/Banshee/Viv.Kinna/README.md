# Viv.Kinna

长连接层。一个 Hub 一套运行实例，同时支持 **SignalR** 和 **原生 WebSocket** 两条传输，
共用一本进程内连接表、一套协议语义、一份准入判定。

**目录**：[定位与边界](#1-定位与边界) · [代码结构](#2-代码结构) · [两条传输](#3-两条传输一张表) ·
[连接生命周期](#4-连接生命周期) · [协议](#5-协议) · [配置](#6-配置) · [接入](#7-接入) ·
[推送](#8-推送) · [踢人](#9-踢人) · [可观测性](#10-可观测性) · [多副本](#11-多副本部署) ·
[关键设计决策](#12-关键设计决策) · [常见坑](#13-常见坑) · [测试](#14-测试)

---

## 1. 定位与边界

**做**：连接登记与租约、群组、上行限流、投递确认（Ack）、重连对齐（Resync）、
推送分发、软踢/硬踢（含跨副本）、健康检查与指标。

**不做**：

| 不做的事 | 原因 |
|---|---|
| 不存消息 | 消息归业务库（Herta 里是 Momo）。Kinna 只记「已确认到哪一条」，不假装能补 |
| 不引 Redis / 消息队列 | 连接表、组表都是进程内状态；跨副本只走一个可选的 `IKinnaKickBus` 端口，实现留在宿主 |
| 不做跨副本路由 | 业务事件按副本全量投递（Nana `ConsumerTopology.PerInstance`），每个副本查本机表，查不到就是「人不在这台机器上」。**路由一旦进缓存，一次 Redis 抖动就变成「连不上」** |
| 不管身份怎么验签 | 验签在 Engine（网关头 / JWT）。Kinna 只定义 `IKinnaIdentityResolver` 端口，把「取出可信身份」交给宿主 |

一句话：**Kinna 管「这条连接还在不在、消息该发给谁」，不管「消息从哪来、存在哪」。**

---

## 2. 代码结构

```
Viv.Kinna/
├── Extensions/          注册与挂载（AddKinna / MapKinnaHub / MapKinnaWebSocket / AddKinnaPusher）
├── Options/             配置项
├── Connections/         生命周期：会话、连接表、协调器、清扫器、限流器、跨副本订阅
├── Dispatch/            推送流程：IKinnaPusher 产出计划，Dispatcher 执行
├── Ports/               宿主实现的端口（身份、准入、群成员、投递存储、在线态、踢人广播）
├── WebSockets/          原生 WebSocket：端点、连接、注册表、协议翻译、分流发送器
├── Contracts/           协议契约：方法名、载荷、信封、Key、连接 Id
├── Metrics/             指标与标签常量
└── Health/              连接数健康检查
```

| 类型 | 职责 | 生命周期 |
|---|---|---|
| `IKinnaSession<THub>` | 一个 Hub 的一套运行时状态：连接表 + 发送器 + Key + 配置 | 单例 |
| `IKinnaConnectionPool` | 进程内连接表，双索引 `(SubjectId, UserId)` 与 `(AppId, SubjectId, UserId)` | 单例 |
| `IKinnaClientSender` | 下行发送口，业务只碰它，不碰 Hub 类型 | 单例 |
| `IKinnaConnectionCoordinator<THub>` | 登记/注销/心跳/确认/对齐/踢人。**不碰 `Hub.Context`**，所以能单测 | Scoped |
| `KinnaWebSocketRegistry<THub>` | 原生连接表 + 组表 + 待发队列 | 单例 |
| `KinnaConnectionLeaseSweeper<THub>` | 定期清超租约连接 | 后台服务 |
| `KinnaKickSubscriber<THub>` | 订阅跨副本踢人指令 | 后台服务 |

**协调器是 Scoped 而不是单例**：它背后可能是 DbContext、Redis 连接。长连接如果把 Scoped
服务攥在手里，那些资源会跟着连接活几个小时。所以原生端点**每条消息开一个作用域**。

---

## 3. 两条传输，一张表

```
接入（两条传输汇聚到同一个协调器）
  SignalR  ──▶ Hub.OnConnectedAsync ──┐
                                      ├──▶ 协调器 ──▶ 连接表（唯一事实源）
  原生 WS  ──▶ KinnaWebSocketEndpoint ┘

下行（发送器按连接归属分流）
  业务事件 ──▶ IKinnaPusher ──▶ Dispatcher ──▶ 发送器 ──┬──▶ 本机原生连接（有界队列）
                                                       └──▶ 其余 ──▶ SignalR
```

两条传输的差别只在**壳**：

| | SignalR | 原生 WebSocket |
|---|---|---|
| 上行 | 方法调用（`Ping()` / `Ack(long[])` / `Resync(long?)`） | JSON 帧 `{"type":"ping"}` … |
| 下行 | `SendAsync(method, payload)` | 信封 `{"method":"…","args":[…]}` |
| 应答 | 方法返回值 | 显式回一帧（`Pong` / `Ack` / `Resync`） |
| 硬断 | 无 API，只能发帧指望客户端自己 `stop` | socket 在服务端手里，发完帧就断 |

分流**查连接表**（`registry.IsLocal`），不看 Id 前缀——前缀只是排障约定，一旦有人换了命名，
SignalR 对未知连接是静默丢弃，消息会发丢而不报错。

`EnableSignalR = false` 时，本机表里查不到的连接没有兜底通道，直接丢弃并记
`kinna.send.orphaned{reason=signalr_disabled}`。

---

## 4. 连接生命周期

### 4.1 建立

```
取身份 → 准入 → 登记 → 回握手帧 → 加组 → 读循环
```

1. **取身份** — `IKinnaIdentityResolver`。拒绝发生在 Upgrade 之前，客户端还能读到 403/400
2. **准入** — `IKinnaAdmission`，两套传输共用同一份实现
3. **登记** — 写连接表。`SingleConnectionPerApp` 时先摘掉同 AppId 的旧连接
4. **回握手帧** — 由 `onRegistered` 回调发出，位置在「已登记、未加组」
5. **加组** — 群成员查询失败只丢群消息，**不断连接**
6. **读循环** — 见 §5.2 的并发模型

**握手帧先于一切业务帧**是硬约束。`RegisterAsync` 的 `onRegistered` 回调正是为此存在：
它被调在「已登记、尚未加组」这一刻。等 `RegisterAsync` 返回再发 `Connected`，
客户端拿到的第一帧就可能是业务消息。

### 4.2 保活

两套心跳，互不替代：

| | 谁发 | 周期 | 作用 |
|---|---|---|---|
| 协议层 ping | Kestrel（`WebSocketKeepAliveSeconds`，默认 30s） | 30s | 发现 TCP 半开（断电、拔网线没有关闭帧） |
| 业务心跳 | 客户端 `ping` | 客户端定，须短于租约 | 续租（`EntryTtlSeconds`，默认 90s） |

超过租约没续租，由 `KinnaConnectionLeaseSweeper` 摘表、扣在线态、软踢。
**去掉 Redis 背板之后连接表没有 TTL**，靠清扫兜底。

客户端 `ping` 时如果表里已经没有它，回 `alive:false` **然后主动关闭**。
只回字段不断开的话，客户端不看这个字段就会一直挂在一条收不到任何消息的连接上。

### 4.3 关闭

关闭顺序是这块最容易被写错的地方，当前实现：

```
MarkClosing → 排空有序队列 → 关闭帧入写队列（封队列）→ 写循环发完
            → 等对端回 Close（CloseHandshakeGrace，默认 200ms）→ Abort/unbind
```

| 场景 | 关闭码 | 说明 |
|---|---|---|
| 客户端发 Close | 1000 / 原样 | 回完关闭帧即断 |
| 软踢（`ForceDisconnect`） | 1000 `disconnected` | 先入队软踢帧，排空后正常关闭。**不用 1012/1008**：客户端该知道是「被人请走」，不是网络抖了一下 |
| 准入/登记失败 | 1008 `PolicyViolation` | 已在 Upgrade 之后，只能用关闭帧表达 |
| 服务端已不认这条连接 | 1008 `connection expired` | 回完 `alive:false` 的 Pong 再断 |
| 上行帧超限 | 1009 `MessageTooBig` | 默认 32KB |
| 待发队列满（慢客户端） | 直接断 | 记 `queue_full`，客户端重连走 Resync |
| 停机 | **1012 Service Restart** | 最多等 500ms 让帧写出去，之后**不 Abort**：留 socket 让对端把帧读走。对端不回 Close 时按 `TerminateGrace`（1s）兜底硬断 |
| 外部打断（请求中止/异常） | 1001 `EndpointUnavailable` | 描述按真实原因给，不一律写 `register failed` |

**为什么先标记 closing 再排关闭帧**：关闭帧一入队，写循环就封队列，
在途的 Ack 回帧只能丢弃（协议上收到 Close 后也不许再发数据帧）。
不先标记的话，这些回帧入队失败会被当成「慢客户端」，走 `Unbind→Abort`，
把还没被客户端读走的关闭握手掐成 `1006`。

**为什么发完关闭帧还要等一帧**：`Unbind` 会立刻 `Abort()` 发 RST，内核把对端还没读走的
关闭帧一起冲掉，客户端只拿到 `1006 / without completing the close handshake`，看不到关闭原因。
`CloseHandshakeGrace`（默认 200ms）内等一帧对端 Close 完成握手，不配合就按上限照常收尾。

**失效通知不能取消 `ReceiveAsync`**：取消一次 WebSocket 读会把 socket 打成 `Aborted`，
后面的关闭帧根本写不出去。现在失效只置 `expiredSignal` 叫醒读循环、并用 `expired`
取消在途指令，socket 的读只跟 `requestAborted`。

**为什么停机要补 1012 而不是直接断**：直接断客户端只拿到 `1006`，
会当成网络故障**立刻**重连，滚动发布期间整个机群一起撞上来。1012 明确告诉它「服务在重启」。

停机和别的关闭路径不一样：帧写出去之后**不能再 `Abort`**。上面那条 `Unbind → Abort` 的 RST
会把这帧一起冲掉（表现和没补帧一模一样：`WSAECONNRESET` / `without completing the close
handshake`），所以停机只摘表、不碰 socket，让读循环等对端 Close 后由 Kestrel 走 FIN 收尾；
对端不读也不回时按 `TerminateGrace` 定时硬断，别让一条连接拖住整次停机。

---

## 5. 协议

### 5.1 下行方法名（两套传输共用）

方法名即事件类型，载荷都继承 `KinnaPayload`（带 `V`，破坏性变更加一；当前恒为 `1`，
没有代码读它做分支）。**客户端约定：不认识的方法和字段一律忽略**——
服务端加方法、加字段时客户端无需改动。

| 方法 | 载荷 | 说明 |
|---|---|---|
| `Connected` | `KinnaConnected` | 握手回执：连接 Id、服务端时间。`ResumeHint` 目前恒为 `null`（框架不存消息，不假装能补），客户端自己去业务 API 对齐 |
| `ReceiveMessage` | 业务定 | 新消息 |
| `MessageRecalled` | 业务定 | 撤回 |
| `ForceDisconnect` | 空 | 软踢，客户端收到后自己 `stop` |
| `Pong` | `KinnaPong` | 心跳应答（仅原生 WS 显式回帧） |
| `Ack` | `KinnaAckResult` | 投递确认结果（仅原生 WS） |
| `Resync` | `KinnaResyncResult` | 重连对齐结果（仅原生 WS） |

### 5.2 原生 WebSocket

**握手**：客户端在 `Sec-WebSocket-Protocol` 里声明 `kinna.v1`，服务端回显**客户端声明的原串**。

**当前只有 v1 一个版本**，`NegotiateSubProtocol` 也只认 `KinnaWebSocketProtocols.V1`，
没有任何多版本分派分支。三条边界：

- **声明 `kinna.v1`** → 握手成功。回显的是客户端写的原串（子协议 token 大小写敏感，
  回常量会让严格客户端拒绝握手）
- **不声明** → 按 v1 处理（没有版本信息时的默认值）；客户端按最新设计一律带上这个头
- **声明了但一个都不认识**（例如将来的 `kinna.v2`）→ `400` + `kinna.ws.rejected{reason=subprotocol}`。
  宁可在 101 之前拒掉，也不让它连上之后按错的格式发帧：那种情况服务端只会当脏帧
  静默丢弃，表现成「连上了但什么都没反应」，比一个 400 难查得多

**上行帧**：

```jsonc
{"type":"ping"}
{"type":"ack","ids":[1024,1025]}
{"type":"resync","lastMessageId":1024}
```

不认识的 `type` 静默忽略（客户端可能发了别的东西，不认不代表恶意）。非文本帧丢弃，
分片缓冲会清空——否则半截文本会和下一条消息粘成脏帧。

**下行帧**：

```jsonc
{"method":"ReceiveMessage","args":[{ "...业务载荷..." }]}
```

用 `method`/`args` 而不是 `type`/`data`，是为了让两种传输的下行语义一一对应：
业务的 `IKinnaPusher` 产出的方法名和载荷原样落到这里，不需要知道客户端用的是哪种传输。

**并发模型**（这是这块的核心设计）：

```
读循环 ──┬── ping          → 旁路，就地派发（最多 4 个在途）
         └── ack / resync  → 有界队列（64），单读者按序处理
                              └─ 队列满：丢弃 + 回一帧 Throttled
```

- 读循环**绝不等指令跑完**，也**绝不等队列腾位置**。
- 队列满时丢的一定要有回执：静默丢弃会让客户端以为服务端收下了，那条 Ack 永不重报，
  水位卡死（表现是重连后一直重复补拉）。
  - `ack` → `{"status":1}`（`Throttled`），稍后重报
  - `resync` → `{"throttled":true,"needBackfill":false}`，**不给补拉信号**——
    限流期间让客户端去补拉，只是把长连接的压力原样转给数据库
- 两个回帧都不碰存储，只编码一帧。

### 5.3 上行结果语义

`KinnaAckStatus` 四态，客户端据此决定要不要重报：

| 值 | 含义 | 客户端动作 |
|---|---|---|
| `Accepted` | 已记录，`Watermark` 有效 | 不必重报 |
| `Throttled` | 超配额或服务端排不下，没记 | 稍后重报 |
| `UnknownConnection` | 服务端不认这条连接 | 重连再报 |
| `Failed` | 落库失败 | 重报 |

`KinnaResyncResult.NeedBackfill`：服务端记录的水位比客户端持有的还大（或压根没水位）时为 `true`，
客户端走 HTTP 增量拉取。`Throttled = true` 时**一定** `NeedBackfill = false`。

---

## 6. 配置

配置来源三层，后者覆盖前者：

1. 共享配置 `VivOptions:KinnaOption`（由 `AddViv` 绑进 DI，全进程一份）
2. 单 Hub 覆盖 `VivOptions:KinnaOption:{Hub 类型名}`
3. `AddKinna<THub>(options => …)` 的代码回调

> 注册期会先 `Clone()` 再绑定。不拷的话两个 Hub 会指向同一个对象，
> 后注册的会把先注册的 `HubPath` / `Prefix` 改掉。

| 配置项 | 默认 | 说明 |
|---|---|---|
| `HubPath` | `/kinna` | SignalR 的映射路径 |
| `Prefix` | `kinna` | 用户 Key / 群名前缀。**同进程两个 Hub 必须不同**，否则串组 |
| `EnableDetailedErrors` | `false` | 是否把异常细节发给客户端，生产关。**进程级**：由 `AddViv` 按共享配置配一次，单 Hub 覆盖不生效 |
| `EntryTtlSeconds` | 90 | 租约时长，超时没 Ping 就被清扫 |
| `SweepIntervalSeconds` | 60 | 清扫间隔 |
| `EnableHealthCheck` | `true` | 连接数健康检查 |
| `EnableSignalR` | `true` | 关掉后 `MapKinnaHub` 变空操作，下行不再走 SignalR |
| `SingleConnectionPerApp` | `true` | 同 AppId 只留一条连接，新连接顶掉旧的（**仅本副本内**） |
| `UplinkPermitLimit` | 20 | 每连接窗口内的上行次数，`0` = 不限流 |
| `UplinkWindowSeconds` | 10 | 配额窗口长度 |
| `DeliveryIdLimit` | 200 | 最多逐个记多少条消息 Id，超出只留水位；存储实现按它决定清理 |
| `WebSocketPath` | 空 | 原生 WS 挂载路径。**空 = 不启用** |
| `WebSocketQueueCapacity` | 512 | 单连接待发队列帧数上限（管「发不出去」） |
| `WebSocketSerialQueueCapacity` | 64 | 有序指令队列深度（管「处理不过来」） |
| `WebSocketMaxMessageBytes` | 32768 | 上行单帧上限 |
| `WebSocketKeepAliveSeconds` | 30 | 协议层 ping 间隔。**进程级**，多 Hub 配不同值启动即报错 |
| `WebSocketCloseTimeout` | 5s | 等关闭帧写出去的上限 |
| `WebSocketTerminateGrace` | 1s | 软踢后排空的宽限 |
| `WebSocketShutdownGrace` | 500ms | 停机补 1012 的等待 |

后三个的关系不能反：`TerminateGrace` / `ShutdownGrace` 必须短于 `CloseTimeout`，
配反了启动直接报错——否则先到的宽限会把还在等的关闭帧判成失败，`close_timeout` 虚高。

> ⚠️ `WebSocketPath` 配的是**下游路径**，不带网关的 `/ws` 前缀。网关把 `/ws/{短名}/**`
> 改写成 `/**`，客户端连 `/ws/hertalink/chat-ws`，到达本进程的是 `/chat-ws`。
> 网关只在 `/ws` 前缀下从 query string 取 `access_token`。

---

## 7. 接入

### 7.1 最小可用（只跑 SignalR）

```csharp
// 注册
builder.Services.AddKinna<ChatHub>();

// 挂载
app.MapKinnaHub<ChatHub>();          // 用 HubPath 配置
```

Hub 自己决定怎么取身份（Kinna 不插手），但**登记必须走协调器**：

```csharp
public class ChatHub : Hub
{
    private readonly IKinnaConnectionCoordinator<ChatHub> _coordinator;

    public override async Task OnConnectedAsync()
    {
        var identity = /* 你的验签逻辑 */;

        var connected = await _coordinator.RegisterAsync(
            Context.ConnectionId,
            identity,
            Context.ConnectionAborted,
            // 握手帧必须在加组前发：加组一完成群推送就可能到达
            frame => Clients.Caller.SendAsync(KinnaClientMethods.Connected, frame));

        if (connected == null)
        {
            Context.Abort();   // 身份缺字段
            return;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _coordinator.UnregisterAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<KinnaPong> Ping() => _coordinator.PingAsync(Context.ConnectionId);
    public Task<KinnaAckResult> Ack(long[] ids) => _coordinator.AckAsync(Context.ConnectionId, ids);
    public Task<KinnaResyncResult> Resync(long? lastMessageId = null)
        => _coordinator.ResyncAsync(Context.ConnectionId, lastMessageId);
}
```

Hub 里只留三件事：**取身份、转发、把结果回给客户端**。其余全在协调器里——
协调器不依赖 `Hub.Context`，所以能直接单测。

### 7.2 加原生 WebSocket

```csharp
builder.Services.AddKinna<ChatHub>(o =>
{
    o.WebSocketPath = "/chat-ws";
    o.EnableSignalR = true;        // 只服务小程序/嵌入式设备时置 false
});

// 原生端点必须注册身份解析：缺了就是没有鉴权，框架一律拒绝（503 + no_resolver）
builder.Services.AddSingleton<IKinnaIdentityResolver<ChatHub>, MyIdentityResolver<ChatHub>>();

app.MapKinnaHub<ChatHub>("/chat");
app.MapKinnaWebSocket<ChatHub>(configure: e => e.RequireRateLimiting("ws"));
```

`MapKinnaWebSocket` 的 `configure` 能挂宿主级横切策略（限流、授权、标签）——
限流和授权都读 endpoint 元数据，没有这个入口这层壳就是策略黑区。
身份与准入仍在端口里判。

两套传输**必须共用一个 `IKinnaAdmission<THub>` 实现**，否则同一个人换个客户端就能绕过限制。

### 7.3 完整清单

| 要做的事 | 必须？ | 不做会怎样 |
|---|---|---|
| `AddKinna<THub>` | ✅ | 什么都起不来 |
| `MapKinnaHub` / `MapKinnaWebSocket` | ✅ | 没有接入点 |
| `IKinnaIdentityResolver<THub>` | 原生 WS 必须 | 端点拒绝所有连接（`no_resolver`） |
| `IKinnaAdmission<THub>` | 建议 | 默认只校验身份完整性，不做业务放行 |
| `IKinnaGroupSource<THub>` | 群消息必须 | 群推送投不到人（连接照常建立） |
| `IKinnaDeliveryStore<THub>` | 要 Ack/Resync 就必须 | 什么都不记，Resync 一律要求补拉 |
| `IKinnaPresenceSink<THub>` | 可选 | 不维护在线态 |
| `IKinnaKickBus<THub>` | 多副本必须 | 踢人只作用于本副本 |

### 7.4 端口一览

| 端口 | 默认实现 | 失败语义 |
|---|---|---|
| `IKinnaIdentityResolver<THub>` | 无 | 返回 `null` 表示取不到身份（**不要抛**，没带 token 是常态） |
| `IKinnaAdmission<THub>` | `KinnaIdentityAdmission`（只校验字段齐） | 返回 `Allowed=false`，`Reason` 只进日志不回客户端 |
| `IKinnaGroupSource<THub>` | 无 | 查询失败只丢群消息，**不断连接**（否则群服务成了 IM 的可用性单点） |
| `IKinnaDeliveryStore<THub>` | `NoopKinnaDeliveryStore` | 抛异常 → 记 `kinna.ack.failed` / `resync.failed`，降级成「重报」/「要求补拉」，不拆连接 |
| `IKinnaPresenceSink<THub>` | `NoopKinnaPresenceSink` | **框架吞异常**，记 `kinna.presence.failed{op}` 后继续 |
| `IKinnaKickBus<THub>` | 无 | 广播失败只记日志和指标，本机已经踢完 |

**在线态为什么只降级**：它是展示信息，不在投递路径上。换一个会抛的实现，
如果直接 `await`，后果是「新连接全建不起来 + 所有心跳拿不到 Pong」——
比 brownout 更彻底，客户端会同时掉线重连。这条约束写在端口契约里，不靠实现自律。

**跨副本通道为什么要串行**：Redis 的接收线程会并发回调，两条指令同时进协调器会
摘两次同一批连接、软踢帧交错写进待发队列。所以 `Subscribe` 的实现**必须**保证
同一时刻只有一个回调在跑（Herta 的实现是：下发线程只入队，另起单读者按序处理）。
而且**绝不能在下发线程上 `await`**——那是全进程共用的，一个频道堵住，别的 Hub 的订阅一起瞎。

---

## 8. 推送

业务只写「事件 → 方法名/载荷/目标」的翻译，流程由 `KinnaMessageDispatcher` 执行：

```csharp
public sealed class ChatMessagePusher : IKinnaPusher<SendMessageEvent>
{
    public KinnaPushPlan? Plan(SendMessageEvent evt, KinnaPushContext? context)
    {
        if (evt.TenantId <= 0 || evt.TargetId <= 0)
            return null;                       // null = 丢弃，记 dropped

        var payload = /* 业务载荷，会原样进 args[0] */ new HertaLinkMessage { /* … */ };

        return evt.ReceiverType == EmChatReceiverType.Group
            ? new KinnaPushPlan("group", KinnaClientMethods.ReceiveMessage, payload,
                                KinnaPushTarget.ToGroup(evt.TenantId, evt.TargetId), evt.FromUserId)
            : new KinnaPushPlan("user", KinnaClientMethods.ReceiveMessage, payload,
                                KinnaPushTarget.ToUser(evt.TenantId, evt.TargetId), evt.FromUserId);
    }
}

// 注册（Hub + 事件两个类型参数，必须按具体类型闭合）
services.AddKinnaPusher<ChatHub, SendMessageEvent, ChatMessagePusher>();
```

投递结果：

| 结果 | 含义 | 调用方处理 |
|---|---|---|
| `Success` | 已发出 | — |
| `Skipped` | 目标不在本副本 | **按成功处理**。每个副本都收到全量事件，查不到是常态 |
| `Failed` | 发送抛异常 | 由消费侧重投 |
| `Dropped` | 计划为空 / 缺租户或群 Id | 不重投，通常说明上游数据有问题 |

**组名只由框架生成**（`IKinnaKeys.GroupName`，带租户前缀）。业务只给租户和群 Id，
自己拼字符串一旦漏了租户，跨租户串组不会报错，只会把消息发给不该看的人。

署名与调用者不一致（`plan.AuthorId != context.UserId`）不算错误——模拟发送、机器人代发
都是正常场景，只记 `kinna.identity.override` 做审计。

---

## 9. 踢人

三档粒度，靠 `UserId` / `AppId` 是否为 0 区分：

| 方法 | 粒度 | 典型场景 |
|---|---|---|
| `KickUserEverywhereAsync(subjectId, userId, reason)` | 该用户**所有**客户端 | 封号 |
| `KickClientEverywhereAsync(appId, subjectId, userId, reason)` | 该用户**某一端** | token 泄露、某端版本有问题要强制重登 |
| `KickSubjectEverywhereAsync(subjectId, reason)` | 整个租户 | 租户停用 |

`*Async`（无 `Everywhere`）= 只踢本副本。

**跨副本的顺序是先踢本机、再广播**：Redis 抖一下就封不了号是最难接受的失败模式。
其余副本收到指令只踢本机连接，并按 `Origin` 跳过自己那份，不成环。

广播失败只记 `kinna.kick.broadcast.failed`，不抛：最坏情况是那些副本的旧连接
再活一个租约周期（90s），到期被清扫摘掉。

**为什么不走业务事件总线**：封号要立刻生效。和业务消息抢同一条队列时，
积压会把「立即」拖成分钟级；而丢一条踢人指令的代价只是多连一个租约周期。

### 单连接

`SingleConnectionPerApp = true`（默认）时，同一 `(AppId, SubjectId, UserId)` 只保留一条连接：
新连接登记时摘掉旧的（扣在线态 + `ForceDisconnect` 软踢），避免重装/重连留下幽灵连接。

**只在本副本内生效**。旧连接在别的副本上不会被顶掉——需要真踢就用 `KickClientEverywhereAsync`。

两套传输共存时，只要 PC 和小程序的 `AppId` 不同就不会互踢；同一用户从两端上线各收一份推送。

---

## 10. 可观测性

指标 Meter 名 **`Viv.Kinna`**，必须加进 `ServiceDefaults` 的 `AddMeter` 名单才会进 OTLP。
所有指标带 `hub` 标签，一个进程装多个 Hub 时能分开看。

| 指标 | 标签 | 读法 |
|---|---|---|
| `kinna.connections` | `hub`, `transport` | 本副本活跃连接数（UpDownCounter） |
| `kinna.dispatch` | `hub`, `kind`, `result` | `result=skipped` 是常态；`failed` 持续涨才是问题 |
| `kinna.uplink.throttled` | `hub`, `method` | **客户端发太快** |
| `kinna.ws.inbound.dropped` | `hub`, `method` | **服务端排不下**（存储慢了）。标签只取 `ack`/`resync`/`other` |
| `kinna.send.orphaned` | `hub`, `reason` | 没写出去就丢的下行帧 |
| `kinna.ws.rejected` | `hub`, `reason` | Upgrade 之前被拒 |
| `kinna.ws.closed` | `hub`, `reason` | 关闭原因分布 |
| `kinna.ack.failed` / `kinna.resync.failed` | `hub` | 上行被存储拖垮（不拆连接） |
| `kinna.presence.failed` | `hub`, `op` | 在线态写入失败（只降级） |
| `kinna.groups.failed` | `hub` | 群成员查询失败（本次没进组） |
| `kinna.identity.override` | `hub` | 代发审计，不是错误 |
| `kinna.kick.remote` / `.dropped` / `.broadcast.failed` | `hub` | 跨副本踢人是否真的接上了 |

`kinna.ws.closed{reason}` 的取值：`client` / `queue_full` / `frame_too_large` / `send_failed` /
`close_timeout` / `expired` / `terminated` / `shutdown` / `shutdown_unsent`。

`kinna.ws.rejected{reason}` 的取值：`not_upgrade` / `no_resolver` / `admission` / `no_identity` /
`register_failed` / `subprotocol` —— 全部发生在 101 之前，客户端拿到的是 HTTP 状态码。

**健康检查** `kinna:{HubName}`，`EnableHealthCheck = false` 时短路成 Healthy。判两件事：

- 清扫超过 3 个周期没跑 → `Degraded`（进程活着但连接会一直涨到租约永远不生效）
- 最久未续租的连接超过 2 倍租约 → `Degraded`（清扫没跟上，或心跳间隔与租约配反了）

**启动自检**（看日志即可判断接线是否完整）：

- 未注册 `IKinnaKickBus` → `Info`：跨副本踢人未启用
- `EnableSignalR=false` 且 `WebSocketPath` 为空 → `Warning`：这个 Hub 没有任何传输可接入
- 已订阅跨副本踢人 → `Info` + 本实例 Id

---

## 11. 多副本部署

```
        业务事件（每副本都收到全量）
                 │
      ┌──────────┼──────────┐
      ▼          ▼          ▼
   副本 A     副本 B     副本 C
   查本机表   查本机表   查本机表
   命中→推    未命中→跳   命中→推
```

- **投递不做路由**：每个副本查自己的连接表，`Skipped` 是正常结果。
- **组表是本进程的**：只有持有连接的副本需要加组，所以 `AddToGroup` 只在本机做。
- **连接状态的跨副本感知只靠租约**：别的副本踢了人，本副本最迟一个租约周期（90s）后清扫掉。
  要立刻生效就走 `IKinnaKickBus`。
- **控制面 vs 数据面**：踢人走可丢的 pub/sub（丢了代价小）；**连接路由绝不放缓存**
  （丢一个路由就是「连不上」）。

---

## 12. 关键设计决策

| 决策 | 为什么 |
|---|---|
| 读循环不在队列上背压 | 一等就是读循环停在原地，排在后面的心跳连读都读不出来，客户端判定掉线重连，重连风暴把存储压得更死——**越忙越断**。丢的是回执不是消息 |
| 队列满一定回 Throttled | 静默丢弃会让客户端以为服务端收下了，那条 Ack 永不重报，水位卡死 |
| 关掉 SignalR 仍调用 `AddSignalR()` | 它的注册发生在配置绑定之前，无法按开关跳过。但关掉后不解析 `IHubContext`、不映射端点，没有对外暴露面 |
| `MapKinnaHub` 关掉时静默返回 | 配置说话，不靠调用方记得清理。漏删也不会把 `/hubs/*` 暴露出去 |
| 分流查表而非看前缀 | 前缀只是约定，改个命名就会把消息发丢，而 SignalR 对未知连接是静默丢弃 |
| 连接表用双索引 | 踢人分三档粒度，没有 `(AppId, SubjectId, UserId)` 就区分不了「只踢某一端」 |
| 协调器不碰 `Hub.Context` | 否则整个生命周期逻辑都进不了单测，只能靠集成测试 |
| 原生端点每条消息开作用域 | Scoped 服务（DbContext / Redis 连接）不能被长连接攥几个小时 |
| 上行限流自己实现 | 框架的 `PartitionedRateLimiter` 分区没有淘汰 API，连接 Id 每次重连都是新的，等于每条历史连接永久占一个分区 |
| 时间统一走 `TimeProvider` | `FakeTimeProvider` 能推进整个模块，租约、清扫、超时都能测 |

---

## 13. 常见坑

**「推送发出去了但没人收到」**
先看 `kinna.dispatch{result=skipped}` 是不是全都在 skipped——那是所有副本都没命中，
通常意味着客户端根本没连上，或者 `Prefix` 两个 Hub 撞了导致串组。

**「客户端连上了但什么都收不到」**
先分清是「没连上」还是「连上了收不到」：`kinna.connections` 有值、`kinna.dispatch{result=ok}`
也在涨，但客户端没反应，就查这三条：

- `kinna.groups.failed` —— 群成员没查到，这条连接这次没进组（单聊不受影响）
- `kinna.ws.closed{reason=queue_full}` —— 客户端读得慢，服务端已经把它断了，客户端还不知道
- 两个 Hub 的 `Prefix` 撞了 —— 不报错，只会串组

**「重启后消息延迟」**
客户端重连前拿到的关闭码如果是 `1006` 而不是 `1012`，说明停机补帧没赶上（硬断把帧冲掉了）。
`WebSocketShutdownGrace` 配太短（200ms 在冷启动时连一帧都写不完，实测偶发拿不到 1012）是一个原因；
另一处是 `AbortAll` 里帧写完之后又 `Abort` —— RST 会把还没被客户端读走的帧丢掉，
停机路径必须走 `Abandon`（不 Abort），别再改回 `Unbind`。

**「Ack 一直不前进」**
看 `kinna.ws.inbound.dropped`：服务端排不下（存储慢）时客户端会一直收到 `Throttled`。
调大 `WebSocketSerialQueueCapacity` 只是把内存换成排队时间，正解是让下游别慢。

**「多 Hub 启动报错」**
`WebSocketKeepAliveSeconds` 是 `UseWebSockets` 的进程级参数，两个 Hub 配了不同值会直接抛。
`Prefix` 撞了不报错，只会串组——更难查，配置时一起检查。

**「跨副本踢人没生效」**
看启动日志有没有 `跨副本踢人已订阅`。没有的话是 `IKinnaKickBus<THub>` 没被扫到；
有的话看 `kinna.kick.remote` 是否增长。

---

## 14. 测试

模块设计成可脱开网络单测：

- 协调器不依赖 `Hub.Context`，直接 new 出来就能测登记/续租/确认/对齐/踢人
- 时间全走 `TimeProvider`，用 `FakeTimeProvider` 推租约和清扫，不必真等
- 原生端点的替身（`RecordingWebSocket` / `BlockingWebSocket` / `FailingBlockingWebSocket`）
  能造出「对端不读」「写循环卡住」「写就抛」这些真实环境才有的时序
- 断言指标时注意：`MeterListener` 是**进程级**的，xUnit 不同测试类并行跑会串台。
  给被测连接传一个独有的 hub 名，按 `hub` 标签过滤

改关闭/丢弃这类路径时，建议**先把用例写好、用旧实现验证它会红**，再改实现——
这些 bug 平时不出现，只在超时和慢存储下出现，靠手测基本抓不到。

### 手工验证：`tools/kinna-console.html`

单文件页面，浏览器直接打开，SignalR 与原生 WebSocket 并排连，用来肉眼对齐「同一个人从两端上线」的行为：

- 上行：`Ping` / `Ack` / `Resync` 各一组按钮，外加自定义方法与原始帧（发非法的 `type` 看服务端怎么忽略）
- 下行：按方法名逐帧打日志，`Connected` / `Pong` 的关键字段（`connectionId`、`resumeHint`、`leaseExpiresAt`）单独摘出来显示
- 断开：显示 `code / reason / wasClean`，并给出关闭码含义——撞 1012 就说明是发布，别当网络故障
- **走网关**：`/ws/{短名}/{**}` → `/{**}` 只剥前缀，页面把 SR/WS/下游/探测四条 URL 一起列出来核对；
  「探测网关」先 POST 一次 negotiate，把 404（路由没生成）、401（token）、403（准入）、429（限流）
  和「请求没出去」（CORS / 混合内容）分开；SignalR 卡在 `Failed to complete negotiation` 时，
  勾「跳过 negotiate」强制走 WebSockets，排除 POST 那一环

两点前置条件：

1. **直连拿不到身份**。Link 是匿名模式（`TokenOption` 为空），浏览器又没法在 WS 升级请求里加
   `x-viv-*` 头，所以 `IKinnaIdentityResolver` 返回 null，握手直接 403。要走网关模式配 JWT：
   `access_token` 只在 `/ws` 前缀下被读取，两种传输都能用。
2. **别用 `file://` 打开**。Origin 是 `file://`，网关 CORS 白名单匹配不上，SignalR 的 negotiate 会被拦；
   在仓库根起个静态服务（`npx serve` / `python -m http.server`）再访问。
