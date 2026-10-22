# Viv.Herta.Link 实时聊天框架重设计（A 路线：MQ 实例广播）

> 状态：设计稿（未动代码）
> 范围：`Viv.Herta.Link` 实时层 + `Viv.Nana` 的**可选**实例广播能力。消息落库、会话查询、seq 分配仍归 `Viv.Herta.Api` / `Viv.Herta.Core`。
> 已定：**A 路线**（每副本独占队列，MQ 广播，每个副本只推自己的本地连接）；推送**携带完整消息体**。

---

## 0. 结论摘要

| # | 决策 | 理由 |
|---|---|---|
| D1 | **每个 Link 副本独占一条队列**，fanout 交换机把消息复制到所有副本；每个副本只推本地连接 | 本地连接表**就是**正确依据；不需要跨副本路由，不需要背板 |
| D2 | **移除 SignalR Redis 背板**（`AddStackExchangeRedis`） | A 与背板是二选一：背板唯一职责是跨进程投递，A 把这个需求整个消掉了；留着等于每次推送白绕一圈 Redis，还把 Redis 塞进实时链路关键路径 |
| D3 | 广播消费者的 `GetConsumeLockKey` **必须返回 `null`**（不加消费锁） | 消费锁 key 含 `ServiceName`，全副本相同 → N 个副本抢同一把锁 → **只有 1 个能进业务，其余丢弃**。不关锁，A 直接失效 |
| D4 | 广播队列固定 `classic` + `AutoDelete` + 非持久 + `ListenerCount=1` + `MaxDegreeOfParallelism=1` | `quorum` 与 `auto-delete` 互斥；串行处理是「单连接有序」的前提 |
| D5 | 实例标识取 `POD_NAME` / `HOSTNAME` / `MachineName+Pid`，**禁止用 `EnvOption.MachineId`** | 现 `appsettings.json` 里 `MachineId: 5` 是写死的，两副本会撞成同一个队列名 → 静默退回竞争消费 |
| D6 | 跨实例协调**全部走 MQ 广播**（消息、成员变更、踢人），Redis 退出实时链路 | 一个机制解决全部跨副本问题，少一个 SPOF |
| D7 | 署名与调用者分离：`evt.FromUserId` 就是署名，可代发；调用者只进审计 | 运营、机器人、系统消息需要以他人名义发（见 §26） |
| D8 | 下行方法名即事件类型，payload 为强类型 record + `V` 版本字段 | SignalR 已有一层方法名，不再套第二层 `type` |
| D9 | `Resync` 只说诚实话：返回本副本水位 + `NeedBackfill`，缺口由客户端走 HTTP 补拉 | Link 不存消息，不能假装能补 |
| D10 | 指标 `Meter("Viv.Herta.Link")` | 与 `MomoMetrics` / `RedisMetrics` 同构 |

---

## 1. 范围与非目标

### 1.1 本期做

- `Viv.Nana`：**可选的**「每实例独占队列」能力（新特性标注 + 队列命名 + 队列属性 + 实例标识解析）。其他服务（Apex / DeepRed / SakuMai）不标注特性 → 拓扑一字节不变
- `Viv.Herta.Link`：连接生命周期、本地租约、投递（本地寻址）、消费端身份校验、踢人、协议契约、群组本地同步、指标
- 运维：旧队列清理、滚动升级期的重复推送处理

### 1.2 本期不做（并说明依赖谁）

| 事项 | 依赖 |
|---|---|
| 会话级 `Seq` 单调序列 | `Herta.Core` 落库改造（协议字段 `Seq` 先留 `null`） |
| 断线期间消息不丢 | 客户端重连后向 `Herta.Api` 增量拉取 |
| 已读回执持久化 | `Herta.Core`（`EtChatSession.LastReadMsgId` / `EtGroupChatMember.LastReadMsgId`） |
| 群成员权威数据源 | `Herta.Core`（本期 Link 只定义接口与缓存） |
| 敏感词、审计、限流（消息侧） | `Herta.Api` 受理层 |

> 本期完成后：**实时投递是正确且有序的**（不漏副本、不乱序、不重复、不冒充），**历史正确性仍由客户端补拉保证**。这个边界要写进客户端对接文档。

---

## 2. 现状与问题清单

### 2.1 现状

`ChatHub` 只做连接登记与加组，`SendMessageConsumer` 只做一次 `Send`；`DefaultGroupService` 恒返回空列表；所有 `Et*` 表在业务代码里**零引用**。

### 2.2 问题分级

| 级别 | 问题 | 位置 | 后果 |
|---|---|---|---|
| **P0** | 同服务多副本共享一条队列（竞争消费），但投递时按**本机字典**查 connectionId | `NanaRegister.GetConsumerQueueName` + `ConnectionPool` | 多副本下**单聊静默漏推**，概率约 `(N-1)/N` |
| **P0** | 消费锁 key 含 `ServiceName`，全副本相同 | `VivConsumer.HandleAsync` | 即使改成广播，也**只有 1/N 生效** |
| **P0** | ~~`FromUserId` 取自请求体~~ | `SendMessageRequest.FromUserId` | 已按 §26 改为「显式代发 + 审计」，不再是漏洞 |
| **P0** | 群成员来源恒为空 | `DefaultGroupService` | 群消息**永远推不到人**，且静默 |
| **P1** | 无本地租约，`OnDisconnectedAsync` 是唯一下线信号 | `ChatHub` | 进程被 kill 后连接表残留，踢人/在线清点失效 |
| **P2** | `SentAt` 在消费端 `UtcNow` 现造 | `SendMessageConsumer` | 重投时间漂移，多端时间线不一致 |
| **P2** | 群聊的 `HertaChatMessage.ToUserId` 塞的是 groupId | 同上 | 字段语义被污染 |
| **P2** | 协议只有 2 个方法常量，无版本、无 ack、无 presence | `HertaLinkClientMethods` | 客户端无法实现重连收敛 |

---

## 3. 设计原则

1. **推送是通知，不是真相。** 只靠推送才能得到的状态，不允许进入协议。
2. **连接可重建。** 客户端任何时刻被杀，重连后靠「已有数据 + HTTP 补拉」收敛。
3. **谁持有连接，谁负责投递。** 每个副本只回答「我这儿有没有这个人」，不问别人。
4. **跨副本只有一个机制：MQ 广播。** 不引入第二套协调通道。
5. **身份只有一个来源。** 信封 `Context` 是唯一身份真相。
6. **重复可容忍，丢失不可容忍。** 协议层吸收重复；不用消费锁换「大概不重复」。

---

## 4. 目标架构

### 4.1 分层

```
┌──────────────────────────────────────────────────────────────────┐
│ Herta.Api / Herta.Core（本期仅 1 行改动）                          │
│   HTTP 受理 → 落库(未来) + 发件箱同事务 → SendMessageEvent        │
└───────────────────────────┬──────────────────────────────────────┘
                            │ RabbitMQ fanout（每实例一条队列）
              ┌─────────────┼─────────────┐
              ▼             ▼             ▼
        Link-1 队列   Link-2 队列   Link-3 队列      ← 每个副本各收一份全量
              │             │             │
              ▼             ▼             ▼
┌──────────────────────────────────────────────────────────────────┐
│ Viv.Herta.Link（每个副本内）                                       │
│                                                                  │
│  Consumers/                                                      │
│    SendMessageConsumer   ──► Dispatch/IMessageDispatcher          │
│    MembershipChangedConsumer ──► Dispatch/IGroupMembershipSync    │
│    UserKickedConsumer    ──► 本地踢人                             │
│                                                                  │
│  Dispatch/                                                       │
│    IMessageDispatcher : 身份校验 → 查本地连接 → 推送 → 记账       │
│    IFanoutPlanner     : (ReceiverType, target) → UserKey/GroupName│
│                                                                  │
│  Hubs/                                                           │
│    ChatHub            : 连接生命周期 / Ping                       │
│    IConnectionPool    : 本地连接表（**唯一投递依据**）             │
│    IKickList          : 本地踢出标记（TTL）                       │
│                                                                  │
│  Contracts/  HertaLinkServerMethods / HertaLinkClientMethods     │
│              HertaLinkMessage / HertaLinkKeys / ...              │
└───────────────────────────┬──────────────────────────────────────┘
                            │ 纯本地
                      各副本本地连接 → WebSocket
```

### 4.2 为什么现在「查本机字典」是对的

A 路线下**每个副本都收到每一条消息**。所以：

> 「本进程连接表里没有这个用户」 == 「这个人不在这台机器上」

不需要问任何别的副本、不需要 Redis 路由表、不需要背板。**本地表从「不完整的缓存」变成了「完整的判断依据」。**

这是 A 的核心价值：把「跨进程路由」这个需求**消掉**，而不是用另一套组件去满足它。

### 4.3 投递时序

```
B(连在 Link-2) ──HTTP──► Herta.Api:POST /api/chat/sendMessage
                            │ [VivUnitOfWork] 落库(未来) + outbox 同事务
                            ▼
                OutboxDispatcher ──► SendMessageExchange (fanout)
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    ▼                     ▼                     ▼
          SendMessageQueue          SendMessageQueue      SendMessageQueue
          .Viv.Herta.Link.a1        .Viv.Herta.Link.b2    .Viv.Herta.Link.c3
                    │                     │                     │
                    ▼                     ▼                     ▼
          A 不在本机 → 空转      B 在本机 → 推送       空转
          （Debug 计数）          （含 B 的其他端）      （Debug 计数）
```

每个副本的处理成本 = 反序列化 + 一次哈希查找 + 命中则推送。**未命中的副本几乎零成本**。

### 4.4 群聊为什么也顺

SignalR 的组成员状态是**每连接**的（存在本进程的连接上下文里）。去掉背板后：

- `Groups.AddToGroupAsync` 只影响本进程连接
- `Clients.Group(name)` 只发给本进程的同组成员

这正是我们要的，而且**成员变更天然只需本副本处理**（§9.2）。

---

## 5. 跨实例机制：MQ 实例广播

### 5.1 声明点：`NanaConsumerAttribute.Topology`

不新增特性，而是扩展现有的 [`NanaConsumerAttribute`](../NanaConsumerAttribute.cs) —— 它本来就不是「调优旋钮」，三个数值属性**全部直接映射到 `RabbitMqQueue`**：

```
ConsumerCount           → queue.ListenerCount
PrefetchCount           → queue.PrefetchCount
MaximumParallelMessages → queue.MaxDegreeOfParallelism
```

它早就是「这个消费者的队列怎么接线」的声明点，广播属于同一类东西，所以放一起，不另起一个特性。

```csharp
public enum ConsumerTopology
{
    Shared = 0,        // 同服务多副本共享一条队列（默认）
    PerInstance = 1    // 每实例独占一条队列（广播）
}

public sealed class NanaConsumerAttribute : Attribute
{
    public ConsumerTopology Topology { get; set; } = ConsumerTopology.Shared;
    // ConsumerCount / PrefetchCount / MaximumParallelMessages 不变
}
```

用法：

```csharp
[NanaConsumer(Topology = ConsumerTopology.PerInstance)]
public class SendMessageConsumer : VivConsumer<SendMessageEvent>
```

**为什么是枚举不是 `bool Broadcast`**：这个开关改的是**队列身份**（名字、类型、持久化全变），属于「模式」不是「叠加一个可选行为」；而且附录 B 已经写明副本数上去以后要迁「按用户分片」，那需要第三档 —— `bool` 到时候只能再加第二个特性。

**默认 `Shared` = 现有行为完全不变**（Apex / DeepRed / SakuMai 零影响）。这一条是硬约束：本仓库所有既有消费者共享同一套拓扑代码，改错一个字节就会把别的服务一起改掉。

### 5.2 队列命名

```
{EventName}Queue.{ServiceName}.{InstanceId}
```

例：`SendMessageQueue.Viv.Herta.Link.a1b2c3d4`

- `ServiceName` 仍是 `NanaRegister.CurrentServiceName`（入口程序集名），保持不变
- `InstanceId` 新增，来源按优先级：

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | `NanaOption.InstanceId` | 显式配置（StatefulSet / 手工多开 / 排查用） |
| 2 | `POD_NAME` 环境变量 | K8s |
| 3 | `HOSTNAME` 环境变量 | Docker |
| 4 | `{MachineName}-{ProcessId}` | 裸机多开；**总非空**，所以不需要 GUID 兜底 |

**必须做名称净化**：只保留 `[A-Za-z0-9._-]`，其余替换为 `-`，去掉首尾分隔符，截断到 64 字节（RabbitMQ 队列名有字符与 255 字节长度约束，且主机名可能带 Unicode）。

**⚠️ 禁止用 `EnvOption.MachineId`**：`appsettings.json` 里写死 `MachineId: 5`，两个副本会得到同一个队列名 → 静默退回竞争消费，而且**没有任何报错**。这是本方案最容易埋进去的坑。

### 5.3 队列属性

在 `VivWolverineConfigurationExtensions` 现有的「直写 `RabbitMqQueue` 属性」那段分支：

```csharp
if (broadcast)
{
    queue.QueueType = QueueType.classic;   // quorum 与 auto-delete 互斥
    queue.IsDurable = false;
    queue.AutoDelete = true;               // 进程断开 → 队列自动回收，不留垃圾
    queue.ListenerCount = 1;               // 单监听
    queue.MaxDegreeOfParallelism = 1;      // 单并行 → 单连接有序的前提
    queue.PreFetchCount = attr?.PrefetchCount > 0 ? attr.PrefetchCount : NanaConsumerAttribute.DefaultPrefetchCount;
                                           // 预取不影响顺序（单并行派发），照常允许调
}
else
{
    // 现有逻辑：prefetch + quorum + [NanaConsumer] 覆盖，一字不改
}
```

> 注册期还会做两条**组合非法**的硬校验（都是静默出错的形状，所以直接抛）：
> `Topology = PerInstance` 标在非 `VivConsumer<T>` 子类上；`Topology = PerInstance` 与 `ConsumerCount` / `MaximumParallelMessages` 同时出现。

| 属性 | 值 | 为什么 |
|---|---|---|
| `QueueType` | `classic` | RabbitMQ 规定 quorum 队列必须 durable/non-exclusive/non-auto-delete，与实例广播的回收语义冲突 |
| `IsDurable` | `false` | 进程不在了，队列留着没有意义 |
| `AutoDelete` | `true` | 最后一个消费者断开即删除；进程崩溃时 AMQP 连接断开也会触发 |
| `ListenerCount` | `1` | 一个副本内再开消费者会引入副本内乱序 |
| `MaxDegreeOfParallelism` | **`1`** | Wolverine 默认 12，**并行处理会打乱单连接顺序** —— 这条不设，§5.4 的有序性完全不成立 |
| `PreFetchCount` | 保持默认 20 | 串行处理下预取只是减少往返，无副作用 |

> `IsExclusive = true` 可以再加一道「同实例名只能有一个消费者」的保险，但它与 Wolverine 的连接/信道复用模型是否稳定需要实测，**默认不开**，作为待验证项记在 §16。

### 5.4 有序性：三层缺一不可

「同一个人的消息按发送顺序到达」由三件事共同保证：

| 层 | 保证 | 失效方式 |
|---|---|---|
| RabbitMQ 队列 | 单队列 + 单生产者 = FIFO | 一旦多个消费者共享队列 → 乱序（**这就是现状**） |
| 队列监听 | `ListenerCount = 1` | 多监听 → 副本内乱序 |
| Wolverine 派发 | `MaxDegreeOfParallelism = 1` | 默认 12 → 并行处理乱序 |

代价是「本副本处理完全串行」。可接受的原因是：**handler 只做内存查找 + `SendAsync`**，没有 IO；真正的慢点是慢客户端，用 §7.5 的超时 + 软踢兜住，避免队头阻塞。

> 若日后实测出队头阻塞：退路是「按 userId 分组、组内串行、组间并行」的 keyed sequencer。**不要**退回提高并行度 —— 那是用正确性换吞吐。

### 5.5 消费锁必须关闭（结构性前置，不是优化）

`VivConsumer.HandleAsync` 默认按 `nana:{ServiceName}:{EventType}:{MessageId}` 取 Redis 锁，抢不到就丢弃。

- **B 路线**：一条队列、一个消费者 → 锁基本是冗余的
- **A 路线**：N 个副本都收到同一条消息、抢**同一把锁** → 只有 1 个进业务，**其余 N-1 个静默丢弃**

所以 `SendMessageConsumer` 必须覆写：

```csharp
/// <summary>
/// 实例广播下不能加消费锁：Key 只含 ServiceName，全副本相同，
/// 抢不到锁的副本会静默丢弃 —— 那正是「只有 1/N 的连接收到消息」的成因。
/// </summary>
protected override object? GetConsumeLockKey(NanaEnvelope<SendMessageEvent> envelope) => null;
```

顺带的好处：**Link 的实时链路对 Redis 零依赖**。

### 5.6 旧队列清理（上线必做）

改造后旧队列 `SendMessageQueue.Viv.Herta.Link` 不再有消费者，但它是 durable 队列，**消息会持续堆积**（吞吐内存）。上线后手工删除：

```bash
rabbitmqctl delete_queue "SendMessageQueue.Viv.Herta.Link"
```

同时要检查有没有别的历史队列名残留（改名前后的都要看一眼）。

### 5.7 滚动升级期的重复推送

升级过程中会同时存在：
- 旧版本副本 → 消费者在旧队列上（共享，竞争消费）
- 新版本副本 → 消费者在各自新队列上（广播）

同一条消息可能被「旧版本的一个副本」和「新版本的每个副本」各推一次。**每连接只归属一个副本**，所以单个连接最多收到 2 份（旧 + 新）。

**这就是客户端必须按 `MessageId` 幂等渲染的原因 —— 它不再是「可选优化」。** 升级窗口控制在分钟级，影响可接受。

---

## 6. 连接层

### 6.1 身份解析（不变的部分保留）

`OnConnectedAsync` 继续只认网关回填的 `x-viv-*` 头，经 `RequestTokenResolver.GetContextFromHeaders` 验签：

```
identity == null || AppId <= 0 || SubjectId <= 0 || UserId <= 0  →  Context.Abort()
```

新增 `deviceId`（query 参数 `device`，仅作**标识**不作身份），缺失时服务端生成并随 `Connected` 下发。

### 6.2 本地连接表

`ConnectionPool` 的角色从「缓存」升为「**唯一投递依据**」，接口保持，语义要在注释里写死：

```csharp
/// <summary>
/// 本进程连接表 —— A 路线下的**唯一投递依据**。
/// 因为每个副本都收到全量消息，「本表没有」就等于「这个人不在这台机器上」，
/// 不需要也不应该去问别的副本或 Redis。
/// </summary>
public interface IConnectionPool { /* 现有签名不变 */ }
```

不新增方法（改名要动接口 + 替身 + 现有用例，收益只是措辞）；只把注释写明是本地语义。
**参数顺序统一为 `AppId, SubjectId, UserId`**，与 `ConnectionKey` / `IVivContext` 一致：

```csharp
void Add(string connectionId, long appId, long subjectId, long userId);
List<string> GetConnectionIds(long subjectId, long userId);            // 按用户（不区分客户端）
List<string> GetConnectionIds(long appId, long subjectId, long userId); // 按用户 + 客户端
```

> 三个都是 `long`，位置写反了编译期毫无提示 —— 整条链路只留这一种顺序，
> 测试里全部用具名参数传，另有一条 `Add_字段顺序不会串位` 专门钉住它。

### 6.3 本地租约（防僵尸连接）

去掉背板后，「进程被 kill 时连接表残留」不再由 Redis TTL 兜底，改为**进程内租约**：

| 参数 | 默认 | 说明 |
|---|---|---|
| `PingInterval` | 20s | 客户端约定 |
| `EntryTtl` | 90s | 超过即视为死连接，从表里剔 |
| `SweepInterval` | 60s | 后台清扫（`IHostedService`，单例，无 Scoped 依赖） |

`ChatHub.Ping()` 续期 + 返回 `serverTime`（客户端据此校正本地时钟）。

**注意**：进程整体崩溃时，表随进程一起消失，本来就不需要清理 —— 租约真正解决的是「个别连接半死」（客户端断网但 TCP 未断）。它比 Redis TTL 便宜得多，也少一次网络往返。

### 6.4 踢人

A 路线下踢人变成**纯本地 + MQ 广播**，反而更硬气：

```
管理员封禁 → 发布 UserKickedEvent（NanaEvent）
                        │ fanout
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
     Link-1           Link-2          Link-3
   查本地连接表    查本地连接表     查本地连接表
   命中 → 下发     命中 → 下发     命中 → 下发
        ForceDisconnect + 标记 revoked(TTL 1h)
```

- `ChatHub` 在任何上行方法（`Ping`/`Ack`/`Typing`/`Resync`）入口先查 `IKickList`，命中即 `Context.Abort()`
- `OnConnectedAsync` 同样先查，命中直接 Abort（防「软踢后立刻重连」）
- `ForceDisconnect` 仍然下发，让客户端主动 `stop()`，体验更好

`IConnectionPool` 现有的 `ForceDisconnectAsync / ForceDisconnectUserAsync / ForceDisconnectTenantAsync` 保留，实现改为本地语义。**`ForceDisconnectTenantAsync` 在集群下只覆盖本副本**，注释里写清楚，运维别指望它踢全租户。

### 6.5 Redis 在本方案里的位置

**实时链路：零 Redis。** 连接路由、组、踢人、租约、去重全在本地或 MQ。

Redis 只在**可选**能力里出现，且明确不进关键路径：

| 用途 | 是否本期 | 说明 |
|---|---|---|
| 在线态查询（`IsOnline`、多端清点） | P2，可选 | 需要跨副本聚合；单靠本地表答不了。启用时用一个轻量注册表，**只读不参与投递** |
| 群成员缓存 | P1 | `IRedisService` 缓存，miss 回源；缓存故障退化直查，不影响推送 |
| 分布式锁 | ❌ | A 路线下必须关闭（§5.5） |

---

## 7. 投递层

### 7.1 寻址

```csharp
public interface IFanoutPlanner
{
    FanoutDecision Plan(SendMessageEvent evt, long tenantId);
}

public sealed record FanoutDecision(FanoutKind Kind, string Target);
// Kind: User(单聊) | Group(群聊)
```

| `ReceiverType` | 目标 | 实现 |
|---|---|---|
| `User` | `HertaLinkKeys.GetUserKey(tenant, target)` | `pool.GetConnectionIds(subjectId, target)` → `Clients.Clients(ids)` |
| `Group` | `HertaLinkKeys.GetGroupName(tenant, target)` | `Clients.Group(name)`（本地组） |

键的单一来源：

```
UserKey(tenant, user)     = $"herta:{tenant}:user:{user}"
GroupName(tenant, group)  = $"herta:{tenant}:group:{group}"
```

### 7.2 署名与调用者是两件事

投递需要的身份分两类，不要混：

| 身份 | 来源 | 用途 |
|---|---|---|
| **谁发的这条事件**（调用者） | 信封 `Context`，发布端从 `IVivContext` 盖章 | 仅用于审计日志，不参与投递 |
| **消息以谁的名义发**（署名） | 事件体 `evt.FromUserId`，0 表示系统消息 | 进消息体、决定客户端怎么显示 |
| **消息属于哪个租户** | 事件体 `evt.TenantId` > 0 优先，否则信封 `Context.SubjectId` | 查连接表、拼群名 |

优先级是「事件体 > 信封」，原因就是要支持模拟：机器人代发、系统消息、运营侧以某个用户的名义推一条，都靠事件体指定租户和署名。这类事件由内网服务直接投 MQ 产生，不经过 `ChatService`。

署名与调用者不一致时**照常投递**，只记一条审计（`以他人名义发送`）并累加 `kinna.identity.override` 指标。这里不再是"防冒充"的关口 —— MQ 是内网可信域，能发事件的服务本来就能代表任何人。

HTTP 入口也能代发，但只限**同租户**：`SendMessageRequest.FromUserId > 0` 就用它，否则用 `IVivContext.UserId`；租户永远取 `IVivContext.SubjectId`，请求体里没有租户字段。跨租户只能走事件，见 §26。

### 7.3 幂等

| 层 | 手段 | 必要性 |
|---|---|---|
| 滚动升级期 | 客户端按 `MessageId` 去重 | **必须**（§5.7 会产生双份） |
| 稳态 | 每个连接只归一个副本、只被推一次 | 无需服务端去重 |
| MQ 重投 | 失败即丢弃（不 `Requeue`），不产生重投 | 无需服务端去重 |

**服务端不做 `SETNX` 去重**：`SETNX` 成功但推送失败的窗口会把消息吞掉，代价比「偶尔重复」高得多。

### 7.4 推送载荷

```csharp
public sealed record HertaLinkMessage
{
    public int V { get; init; } = 1;              // 协议版本
    public long MessageId { get; init; }          // 幂等键
    public long? Seq { get; init; }               // 依赖 Core 落库；本期恒 null
    public long TenantId { get; init; }
    public long? AppId { get; init; }
    public long FromUserId { get; init; }
    public long ToUserId { get; init; }           // 单聊=对方；群聊=0
    public long? GroupId { get; init; }           // 群聊时填
    public EmChatReceiverType ReceiverType { get; init; }
    public EmChatMessageType MessageType { get; init; }
    public IHertaChatMessage? Body { get; init; }
    public DateTimeOffset SentAt { get; init; }   // 来自事件/存储，不在消费端现造
    public long? ReplyMsgId { get; init; }        // 预留
    public string? TraceId { get; init; }
}
```

相对现状的修正：`ToUserId` 不再塞 groupId（改用 `GroupId`）、`MessageId` 提为显式字段、加 `V` / `TraceId`。

> **`SentAt` 取 `NanaEnvelope.CreatedAt`**（入发件箱那一刻盖章），重投时信封原样传递所以不会漂，
> 因此**不需要**给 `SendMessageEvent` 加字段 —— 原 §16 Q1 已按此关闭。

### 7.5 失败与背压

| 场景 | 处理 |
|---|---|
| 目标不在本机 | 正常成功，`dispatch.skipped` 计数（Debug）。**不是失败** |
| 对方完全离线 | 同上。补拉由客户端负责 |
| `SendAsync` 抛异常 | 记 Error，返回失败**丢弃**（不重投：重投仍会失败，且客户端会补拉） |
| 慢客户端 | 单次 `SendAsync` 套 `SlowClientTimeout`（默认 5s），超时 → 记 Warning + 软踢。串行处理下这是**唯一的队头阻塞来源**，必须超时 |
| 大群 | 仍走 `Clients.Group`（本地一次遍历，成本与本地成员数成正比） |

---

## 8. 协议契约

### 8.1 命名

- 方法名 = 事件类型（PascalCase），单一来源在两个常量类里
- payload = 强类型 record，必带 `V`
- 未知方法 / 未知字段：客户端**必须忽略**

### 8.2 下行（服务端 → 客户端）

| 方法 | 载荷 | 说明 |
|---|---|---|
| `Connected` | `HertaLinkConnected` | `connectionId` / `serverTime` / `protocolVersion` / `resumeHint` |
| `MessageReceived` | `HertaLinkMessage` | 新消息（完整体） |
| `MessageRecalled` | `HertaLinkRecall` | 撤回（预留，Core 未实现） |
| `SessionChanged` | `HertaLinkSessionChanged` | 会话变更（置顶/免打扰/新会话） |
| `ForceDisconnect` | `HertaLinkForceDisconnect` | 软踢：`reason` |
| `PresenceChanged` | `HertaLinkPresence` | 在线态（P2，可选） |

> `Ping` **不需要** `Pong` 下行事件：hub 方法可以直接返回值给调用方，天然配对。`Pong` 只作为扩展位保留。

### 8.3 上行（客户端 → 服务端）

| 方法 | 参数 | 说明 |
|---|---|---|
| `Ping` | 无 | 返回 `HertaLinkPong { serverTime, leaseExpiresAt }`，同时续租 |
| `Ack` | `long[] messageIds` | 投递确认，记该用户已确认到的最大 MessageId，返回水位 |
| `Resync` | `long? lastMessageId` | 重连对齐，返回服务端水位 + 是否需要补拉 |

**不做**：hub 内发消息。发送一律走 HTTP（可审计、可限流、可入发件箱）。hub 只承担会话级瞬态信号。

### 8.4 `Ack` 与 `Resync`

两者共用一份状态：**每个用户「已确认的消息 Id」+ 一条水位**。

```csharp
public sealed record KinnaResyncResult : KinnaPayload
{
    public long? LastDeliveredMessageId { get; init; }  // 水位，没记录为 null
    public long[] DeliveredMessageIds { get; init; }    // 逐个记下的 Id，记满后为空
    public bool NeedBackfill { get; init; }
    public DateTimeOffset ServerTime { get; init; }
}
```

规则：

- `Ack(long[] messageIds)` 逐条记 Id。**记到 `KinnaOptions.DeliveryIdLimit` 条就全清，只留水位**，等价于「全部标记已收到」；上限的意义是别让活跃用户的集合无限涨。配 `0` 表示从不记 Id，只留水位。配置按 Hub 归属，`KinnaOptions:<Hub 类型名>` 可单独覆盖。
- 水位只进不退。客户端乱序补报、或重连后重放旧消息，都不会往回拨。
- 身份从连接表取，不接受客户端自报 `userId`，否则可以替别人确认。
- 存储走 `IKinnaDeliveryStore<THub>` 端口，Kinna 不绑实现；Herta 落 Redis：一个 Hash，字段 `w` 是水位，其余字段名就是消息 Id（`{prefix}:{subjectId}:delivered:{userId}`，保留 7 天）。
- `Resync` 的判断只有一条：**水位比客户端持有的还大 → 需要补拉**。没有水位记录时保守返回 `NeedBackfill = true`，增量拉取幂等，多拉一次比漏消息便宜。`DeliveredMessageIds` 交给客户端在补拉时跳过。
- Redis 挂了：`Ack` 吞掉异常并记 Warning（返回旧水位），`Resync` 读到空状态从而要求补拉。客户端不会因为缓存故障拿不到连接。
- 不记明细。明细级确认（哪条消息读了）走 HTTP 的已读接口，落在核心库。

### 8.5 版本化

- `V` 单调递增，只增不改语义；破坏性变更走 `V+1`，过渡期可同时下发两种
- 客户端在连接 query 里上报协议版本，服务端记进连接表，便于灰度与排障
- `protocolVersion` 在 `Connected` 里回显，不匹配记 Warning 但不拒连

---

## 9. 群组

### 9.1 连接加组

```
OnConnectedAsync → IGroupMembershipProvider.GetUserGroupIdsAsync(tenant, user)
                 → 逐个 Groups.AddToGroupAsync(connectionId, GroupName(tenant, groupId))
```

- 接口由 `Herta.Core` 定义（可沿用 `IGroupService`），Link 侧实现带 `IRedisService` 缓存（TTL 5min，miss 回源）
- 缓存故障 → 记 Warning 后回源；回源也失败 → **只加 0 个组并记 Error**，不能静默

> **现状必须改掉**：`DefaultGroupService` 恒返回空列表、不报错、不打日志。**静默失效比报错糟糕得多。** 本期若仍无数据源，应在启动时打一条 Error 明确「群消息投递不可用」。

### 9.2 成员变更（广播 + 本地生效）

新增 `MembershipChangedEvent : NanaEvent`，`MembershipChangedConsumer` 的 `Topology` 设为 `PerInstance`：

```
每个副本各自处理：
  ① 删 Redis 成员缓存键（下次回源）
  ② pool.GetConnectionIds(subject, user) → AddToGroupAsync / RemoveFromGroupAsync
```

**每个副本都收到、都只处理自己那部分** —— 不需要任何跨副本协调，也不需要「告诉某台机器去做」。

### 9.3 退群 / 踢人 / 解散

| 事件 | 动作 |
|---|---|
| 成员退群 | 本地 `RemoveFromGroupAsync` + 删缓存 |
| 被踢 | 同上 + 下发 `SessionChanged` |
| 群解散 | 对该群本地所有连接 `RemoveFromGroupAsync` + 缓存失效（成员快照来自 Core） |

---

## 10. 配置

`NanaOptions` 新增：

```jsonc
"NanaOption": {
  "InstanceId": null   // 可选覆盖；null = 按 POD_NAME/HOSTNAME/MachineName+Pid/GUID 推导
}
```

`HertaLinkOptions` 扩展（**当前 `appsettings.json` 里根本没有这个节点**，绑定走默认值，落地时要补）：

```jsonc
"HertaLinkOptions": {
  "HubPath": "/chat",
  "EnableDetailedErrors": false,

  "ProtocolVersion": 1,
  "EntryTtlSeconds": 90,
  "SweepIntervalSeconds": 60,
  "SlowClientTimeoutSeconds": 5,

  "MaxConnectionsPerUser": 10,
  "UpstreamRateLimitPerSecond": 5,

  "PresenceEnabled": false,          // P2，需要跨副本聚合，默认关
  "RedisKeyPrefix": "viv:herta:link"
}
```

约定：所有时间用**秒**（与现有配置风格一致）；新增项必须有默认值，**不配也能启动**。

---

## 11. 可观测性

### 11.1 指标（`Meter("Viv.Herta.Link")`）

| 指标 | 类型 | 标签 |
|---|---|---|
| `herta.link.connections` | UpDownCounter | — （本副本活跃连接） |
| `herta.link.dispatch` | Counter | `kind=user\|group`, `result=ok\|skipped\|failed` |
| `herta.link.dispatch.duration` | Histogram(ms) | `kind` |
| `herta.link.slow.client` | Counter | — |
| `herta.link.identity.mismatch` | Counter | — （**应恒为 0**） |
| `herta.link.kick` | Counter | `scope=user\|connection` |

`Viv.Aspire.ServiceDefaults` 的 `AddMeter` 名单是**手写死的**（`Viv.Outbox` / `Viv.Nana` / `Viv.Redis` / `Viv.Momo`），不加 `Viv.Herta.Link` 指标上不了 OTLP。

### 11.2 日志

| 级别 | 事件 |
|---|---|
| Information | **启动时打印本实例队列名**（运维排查第一手信息） |
| Debug | 未命中本机、Ack 统计 |
| Warning | 身份不符、慢客户端、成员缓存降级、协议版本不匹配 |
| Error | 推送失败、群成员源为空、实例标识冲突（同名队列已存在消费者） |

---

## 12. 安全清单

- [x] 身份只来自 `x-viv-*`（网关验签），query 参数不参与鉴权
- [x] 发送者身份以信封 `Context` 为准（§7.2）
- [x] 群组名带 tenant，杜绝跨租户串组
- [ ] 上行限流（`Ping`/`Typing`/`Ack` 每连接令牌桶，默认 5/s、突发 20）
- [ ] 每用户连接数上限（`MaxConnectionsPerUser`，超限踢最旧）
- [ ] `Resync` 只返回本连接记忆，不泄露他人数据

---

## 13. 文件级清单

### `Viv.Nana`（框架层，默认行为零变化）

```
ConsumerTopology.cs                       **新增**：Shared / PerInstance 枚举（独立文件）
NanaConsumerAttribute.cs                  改：新增 Topology 属性 + 数值属性边界注释
NanaRegister.cs                           改：InstanceId 解析 + 队列名带实例后缀 + BroadcastQueues
Options/NanaOptions.cs                    改：InstanceId
Core/VivWolverineConfigurationExtensions.cs  改：广播分支（classic/auto-delete/单监听/单并行）+ 组合非法校验
```

> 代码已落地，**尚未验证**（未 build）。预期映射：`[NanaConsumer]` 缺席 / `Topology = Shared` / `PerInstance`
> 三种情况下的队列名分别为 `…{Service}` / `…{Service}` / `…{Service}.{InstanceId}`。

### `Viv.EventContracts`

```
Herta/SendMessageEvent.cs                 无需改动（SentAt 取信封 CreatedAt）
Herta/MembershipChangedEvent.cs           新增
Herta/UserKickedEvent.cs                  新增
```

### `Viv.Herta.Link`

```
Contracts/HertaLinkKeys.cs                UserKey / GroupName 单一来源
Contracts/HertaLinkServerMethods.cs       下行方法名
Contracts/HertaLinkClientMethods.cs       上行方法名（现有文件扩写）
Contracts/HertaLinkMessage.cs
Contracts/HertaLinkConnected.cs
Contracts/HertaLinkResyncResult.cs
Contracts/HertaLinkTyping.cs

Hubs/ConnectionDescriptor.cs              （扩写现有 ConnectionInfo）
Hubs/IKickList.cs + InMemoryKickList.cs
Hubs/ConnectionLeaseSweeper.cs            IHostedService（无 Scoped 依赖）

Dispatch/IFanoutPlanner.cs + FanoutPlanner.cs
Dispatch/IMessageDispatcher.cs + SignalRMessageDispatcher.cs
Dispatch/IGroupMembershipSync.cs

Consumers/SendMessageConsumer.cs          改调 dispatcher；Topology = PerInstance；关锁
Consumers/MembershipChangedConsumer.cs    新增
Consumers/UserKickedConsumer.cs           新增

Metrics/HertaLinkMetrics.cs
```

### 修改

```
Hubs/ChatHub.cs                           身份 + 租约 + Connected/Ping/Ack/Resync + revoked 检查
Hubs/ConnectionPool.cs                    注释写明「唯一投递依据」；本地踢人；租约剔除
Hubs/HertaLinkGroups.cs                   转发 HertaLinkKeys（保留 [Obsolete]）
Hubs/IConnectionPool.cs                   改注释 + 参数顺序统一为 AppId, SubjectId, UserId
Extensions/HertaLinkServiceCollectionExtensions.cs   **移除 AddStackExchangeRedis**；注册新服务
Options/HertaLinkOptions.cs               新增配置项
appsettings.json / appsettings.Development.json     补 HertaLinkOptions 节点
```

### 测试

- `Viv.Nana.Tests`：实例队列命名、名称净化、默认拓扑下行为不变、`InstanceId` 解析优先级
- `Viv.Herta.Tests`：planner / dispatcher（命中与未命中）/ 身份不符 / 踢人 / 成员同步
- **`Viv.Fakes` 不需要改**：A 路线不用 `Clients.User` / `Clients.Users`，`FakeHubClients` 里那两个 `NotImplementedException` 保持原样就是最好的守卫（真被走到说明又退回了 B）
- 新增 `ChatHub` 测试夹具，或把「身份解析 + 连接登记」抽成 `ConnectionCoordinator` 以便纯单元覆盖（推荐后者）

---

## 14. 分阶段落地

### P0 — 结构性改造（必须一次上齐，缺一不可）

1. `Viv.Nana`：实例广播队列（命名 + 属性 + 实例标识）
2. `SendMessageConsumer`：`Topology = PerInstance` + `GetConsumeLockKey → null`
3. **移除** `AddStackExchangeRedis`
4. ~~`ChatService`：`FromUserId` 用 `IVivContext.UserId` 覆盖~~ —— 已按 §26 改为「不填用登录身份，填了代发」
5. `SendMessageConsumer`：身份一致性校验
6. 载荷修正（`HertaLinkMessage`）
7. 运维：删旧队列、确认新队列按副本数生成

> P0 的 1/2/3 是**一个原子单元**：只做 1 不做 3 → 背板仍在，重复投递；只做 1/3 不做 2 → 只有 1/N 生效。

### P1 — 连接可信

8. 本地租约 + 清扫
9. 协议：`Connected` / `Ping` / `Ack` / `Resync`
10. 踢人（`UserKickedEvent` + `IKickList` + 本地 Abort）
11. 指标 + `AddMeter`

### P2 — 群组与治理

12. `MembershipChangedEvent` + 本地组同步
13. 群成员真数据源（依赖 Core）
14. 上行限流、连接数上限
15. presence（可选，需跨副本聚合）
16. 与 Core 的 `Seq` 对齐

---

## 15. 测试计划

| 层 | 用例 | 手段 |
|---|---|---|
| Nana | `Topology = PerInstance` → 队列名带实例后缀；默认 `Shared` → **与原名称逐字相同** | 纯单元（`NanaRegister`） |
| Nana | 实例标识优先级、非法字符净化、长度截断 | 纯单元 |
| Planner | 单聊/群聊/自聊 决策 | 纯单元 |
| Dispatcher | 命中推送、未命中跳过且成功、`SendAsync` 抛异常不重投 | `FakeHubContext` |
| Dispatcher | 身份不符 → 丢弃 + 计数 | `RecordingLogger` |
| Hub | 无身份 Abort、revoked 拒绝、Ping 续租 | 需夹具（或经 `ConnectionCoordinator` 覆盖） |
| 契约 | 载荷字段齐全、`V` 存在 | 纯单元 |

---

## 16. 风险与未决问题

| # | 问题 | 影响 | 建议 |
|---|---|---|---|
| R1 | **N 倍 MQ 流量**（副本数 ×消息量） | 副本到 20+ 时明显 | 到那时迁 **B2：按 userId 分片队列**（同一用户永远进同一分片 → 仍有序），而不是回到 Redis 背板 |
| R2 | 广播队列只能 `classic`，失去 quorum 的持久性 | 进程崩溃期间打到该副本的消息丢失 | 可接受：那时它上面的连接已断，客户端补拉 |
| R3 | `MaxDegreeOfParallelism = 1` 的队头阻塞 | 一个慢客户端可能拖慢本副本 | 严格 `SlowClientTimeout` + 软踢；实测不达标再上 keyed sequencer |
| R4 | 实例标识撞名（配置写死 `MachineId`、容器 hostname 重复） | **静默**退回竞争消费，只在指标上表现为漏推 | 启动日志打印队列名 + 同名队列已有消费者时记 Error；`InstanceId` 显式配置做兜底 |
| R5 | `AutoDelete` 的回收时机未实测（进程崩溃 vs 正常断开） | 极端情况下留孤儿队列 | 上线后观察 `rabbitmqctl list_queues`；必要时加 `x-expires` |
| R6 | `IsExclusive = true` 与 Wolverine 连接复用是否稳定 | 未验证 | 先不开；需要「同名实例互斥」时再实测 |
| Q1 | ~~`SendMessageEvent` 加 `SentAt`~~ | — | **已关闭**：改用 `NanaEnvelope.CreatedAt`，零跨层改动 |
| Q2 | 群成员数据源本期仍未落地 | 群聊不可用 | 补只读 Repository，或显式禁用并报 Error |
| Q3 | 多端「已读」走 hub `Ack` 还是 HTTP | 协议面 | 走 HTTP（要落库），`Ack` 只做投递确认 |
| Q4 | 滚动升级期的双份推送窗口 | 客户端重复气泡 | 靠客户端 `MessageId` 去重吸收；升级窗口控制在分钟级 |

---

## 17. 实现记录（P0 落地时与草稿的偏差）

代码已落地、**未 build**。落地过程中发现并处理了四件草稿里没写对的事：

### 17.1 `NanaOption.ConsumerTypes` 是空的 —— 整条推送链路在配置层就是断的

`Viv.Herta.Link/appsettings*.json` 里 `"ConsumerTypes": []`，而 `NanaRegister.ScanConsumerTypes` 对空清单直接返回空集 →
`AddVivWolverine` 的消费方循环一次都不跑 → **`SendMessageConsumer` 从未被注册、队列从未声明、消息从未被消费**。

这比「多副本漏推」更靠前：整套东西之前根本没跑起来。已按 `Apex.Worker` / `Ouroboros.Worker` 的形状补上：

```jsonc
"ConsumerTypes": [
  { "AssemblyName": "Viv.Herta.Link", "Namespace": "Viv.Herta.Link.Consumers", "ClassNameEndsWith": "Consumer" }
]
```

> **新增消费者时必须同时检查这一节** —— 扫描不到就是静默不消费，没有异常、没有日志。

### 17.2 消费者的构造形状：直接注入 `IMessageDispatcher`

当前实现是 `VivConsumerDependency` + `IMessageDispatcher` 直连注入。

`OuroborosTurnConsumer` 用的是「只吃 `VivConsumerDependency` + `IServiceScopeFactory`，每轮现开作用域」，
但那是**因为它的业务服务只注册在 Autofac 里**（Wolverine 只看得见 `IServiceCollection`）。
本项目的 `IMessageDispatcher` 注册在 `IServiceCollection`，属于 Wolverine 看得见的那一层。

**未验证的残余风险**：`SignalRMessageDispatcher` 依赖 `IHubContext<ChatHub>`，其实现由 `AddSignalR` 工厂注册、
ctor 不公开 —— 若 Wolverine 内联构造走到这一层，会退回服务定位并被 `ServiceLocationPolicy.NotAllowed` 拒绝，
表现是**消息全部进死信**。

验证方式：投一条消息，看是正常推送还是进 `wolverine-dead-letter-queue`。
若进死信，退回 `IServiceScopeFactory` 那版（每轮现开作用域解析 dispatcher）。

### 17.3 `Body` 必须声明成 `object`，不能是 `IHertaChatMessage`

System.Text.Json 按**声明类型**选契约：声明成接口时只序列化接口成员（就一个 `MessageType`），
`TextMessage.Text` 整个丢掉。声明成 `object` 才按运行时类型序列化。

**这意味着旧版 `HertaChatMessage.Body` 的客户端其实从来没拿到过正文** —— 又一处「静默但致命」。

### 17.4 相对草稿的收敛

| 草稿 | 实际 | 原因 |
|---|---|---|
| `IConnectionPool` 新增 `GetLocalConnectionIds` | **不加**，只把现有 `GetConnectionIds` 的注释写明是本地语义 | 改名要动接口 + 替身 + 现有用例，收益只是措辞 |
| 群名 `herta:{tenant}:group:{id}` 由 `HertaLinkGroups` 产出 | 产出移到 `HertaLinkKeys`，`HertaLinkGroups` 保留为转发 | 规则只能有一份；旧入口保留是为了不动既有调用点 |
| 连接身份 `(TenantId, UserId, AppId)` | `(AppId, SubjectId, UserId)` | 与 `IVivContext` / `VivContextContent` 同序同名，照着上下文取、照着上下文传不会错位 |
| `ForceDisconnectTenantAsync(tenantId)` | `ForceDisconnectSubjectAsync(subjectId)` | 参数已经是 `subjectId`（值就是 `IVivContext.SubjectId`），方法名再叫 Tenant 就是同一个签名里混两个词 |

> 连接身份的顺序变更涉及 `ConnectionKey` / `ConnectionInfo` / `IConnectionPool` / `ConnectionPool` /
> `ChatHub` 调用点 / `StubConnectionPool` / 测试。三个字段全是 `long`，**位置写反不会有任何编译期提示**，
> 所以测试里一律用具名参数，并留了一条 `Add_字段顺序不会串位` 专门钉住。

---

## 18. P1 落地：协议 + 租约 + 群成员数据源

### 18.1 群成员终于有了数据源

`DefaultGroupService` 恒返回空列表 → 群消息推给一个没有成员的组，静默无效。已删除，换成：

```
Viv.Herta.Core/IRepository/IChatGroupMemberRepository.cs      GetGroupIdsAsync / IsMemberAsync
Viv.Herta.Core/Repository/ChatGroupMemberRepository.cs        查 EtGroupChatMember，QuitAt == null 视为在群
Viv.Herta.Core/Service/GroupService.cs                        实现 IGroupService（DIOption 扫到即自动绑定接口）
```

**没有加 Redis 缓存**：成员只在连接建立时查一次，一次索引查询；缓存要换成员变更时的失效逻辑，
在没有压测数据前不值得。文档上一版写的 5 分钟缓存，改为「有了再做」。

### 18.2 协议面

| 方向 | 方法 | 载荷 | 本期行为 |
|---|---|---|---|
| 下行 | `Connected` | `HertaLinkConnected` | 连接登记完成后下发 connectionId / serverTime |
| 下行 | `ReceiveMessage` | `HertaLinkMessage` | 新消息 |
| 下行 | `ForceDisconnect` | — | 保留 |
| 上行 | `Ping()` | 返回 `HertaLinkPong` | 续租 + 返回 serverTime / LeaseExpiresAt |
| 上行 | `Ack(long[])` | 返回当前水位 `long` | 记录已确认的最大 MessageId（Redis，7 天） |
| 上行 | `Resync(long?)` | 返回 `KinnaResyncResult` | 用已确认水位判断要不要补拉（见 §8.4） |

`Connected` 的 `ResumeHint` 本期恒 null（本副本不记投递水位，记它要写放大，先不做）。

### 18.3 连接租约

删掉背板后，连接表不再有 Redis TTL 兜底：客户端断网但 TCP 未断时 `OnDisconnectedAsync` 不会触发。

- `ConnectionPool` 每条连接记 `lastSeen`，`Touch` 续租、`SweepExpired` 剔除
- `ConnectionLeaseSweeper : BackgroundService` 按 `SweepIntervalSeconds` 清扫
- `HertaLinkOptions.EntryTtlSeconds`（默认 90）/ `SweepIntervalSeconds`（默认 60）

### 18.4 健康检查与上行限流

**健康检查**（`KinnaConnectionHealthCheck<THub>`，`KinnaOptions.EnableHealthCheck` 默认开）挂在标准 `/health` 上，一个 Hub 一条：

- 报告连接数、最久未续租时长、距上次清扫多少秒
- 清扫还没跑过、或超过三个周期没跑 → `Degraded`（进程活着但租约不受控）
- 最久未续租超过两倍租约 → `Degraded`（清扫没跟上，或心跳间隔与租约配不上）

清扫循环每跑完一轮把时间写进 `KinnaSweepClock<THub>`，健康检查读它，所以「后台任务卡死」能被发现。

**上行限流**（`KinnaUplinkLimiter<THub>`，`KinnaOptions.UplinkPermitLimit` 默认 20 次 / `UplinkWindowSeconds` 默认 10 秒）：

- 按连接计固定窗口，只挡 `Ping` / `Ack`
- 被挡下的 `Ping` 仍返回 Pong（只是不续租），被挡下的 `Ack` 返回 0，都不抛错、不惩罚客户端
- 计 `kinna.uplink.throttled{hub, method}`
- 配 0 关闭限流

### 18.5 可测性调整：Hub 只剩转发

身份解析 + 登记 + 加组 + 协议应答全部搬进 `IConnectionCoordinator`，`ChatHub` 的每个方法都是一行转发。
这样核心逻辑不需要 `Hub.Context` 夹具就能覆盖（`ConnectionCoordinatorTests`）。

---

## 19. 落库 + 拉取（发消息的另一半）

### 19.1 发送：落库与入队同事务

`ChatService.SendMessageAsync` 现在做三件事，全在 `[VivUnitOfWork]` 一个事务里：

1. `IChatSessionRepository.GetOrCreateAsync` —— 按「当前用户 + 对端」取或建会话（群聊每个成员各一行）
2. `IChatMessageRepository.AppendAsync` —— 单聊写 `EtUserChatMessage`、群聊写 `EtGroupChatMessage`
3. `IVivOutbox.EnqueueAsync` —— 事件带上落库行 Id

**消息体原样存客户端传来的 JSON**（`Content` 列），不按 `MessageType` 拆成各字段 —— `MixMessage` / `CommandMessage` 拆了就丢信息。`MsgType` 列用来解释这段 JSON。

`SendStatus` 留在默认 0（待投递）：投递结果目前无人回写，这是已知缺口。

### 19.2 推送与拉取用同一个 Id

`SendMessageEvent` 加了两个字段：`MessageId`（落库行 Id）与 `SentAt`（落库时间）。

- 推送的 `HertaLinkMessage.MessageId` = **落库行 Id**，不再是信封的传输 Id
- 拉取返回的 `MessageItemOutput.Id` = 同一个值
- `SentAt` 两边同源，客户端混用两条通道排序不会抖

`evt.MessageId == 0` 时退回信封 Id（升级窗口内的在途消息），否则丢推更糟。

### 19.3 拉取

```
GET /api/chat/messages?receiverType=&targetId=&afterMessageId=&beforeMessageId=&limit=
```

三种模式，返回**一律升序**：

| 游标 | 含义 | 排序 |
|---|---|---|
| `afterMessageId > 0` | 向后增量（重连补拉） | 升序取 N |
| `beforeMessageId > 0` | 向前翻历史（上滑加载） | 降序取 N，再反转 |
| 两个都不给 | 首屏取最新 N 条 | 降序取 N，再反转 |

两个游标同时给会被 `Validate()` 拒掉。

`EtChatSession` 是**每用户一份**的行（自己的未读、置顶、最后消息时间），
而 `EtUserChatMessage` / `EtGroupChatMessage` 的 `SessionId` 记的是**发送方**的 session。
如果拉取时先 `FindAsync(我, 对端)` 再按 session 过滤，就会出现：

```
A → B 先发了一条：建 A 的 session(100)，消息 SessionId = 100
B 拉取：FindAsync(B, 对端=A) → 没有这行 → 返回空
```

也就是**接收方永远拉不到历史**，只能靠实时推送。所以 `ListAsync` 的签名是
`(tenantId, receiverType, userId, targetId, afterMessageId, beforeMessageId, limit)`：

- 群聊：按 `GroupChatId` 直接查
- 单聊：取「我发给他的」与「他发给我的」两个方向的并集

**排序与截断都在数据库里做**：这段读走手写 SQL，不是 EF 谓词查询。

EF 的 `FindListAsync(predicate)` 只能捞全量，想「按 Id 排序 + 只取 N 条」要么先捞全量再内存排（首次拉取
会读出一整个会话），要么给 `IMomoDbContext` 加排序重载。后者加完发现一件事：
`SqlMagic` 的静态构造早就打开了 `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true`，
**PostgreSQL 的 `snake_case` → PascalCase 映射本来就是通的**，手写 SQL 没有映射风险。

于是用仓里现成的两个助手拼（也就不用给框架加 API）：

```
表名  SqlMagic.GetTableName<T>(databaseSource)     → et_user_chat_message / [EtUserChatMessage]
列名  SqlMagic.QuoteIdentifier(name, databaseSource) → user_id / [UserId]
取前 N SQL Server 用 TOP (@limit)，PostgreSQL 用 LIMIT @limit —— 只有这一处方言差异
```

两个必须手写带上的条件（原生 SQL 不过全局过滤器）：`TenantId = @tenantId` 与 `IsDeleted = @notDeleted`。
**软删标记用 bool 参数而不是 `= 0`** —— PostgreSQL 的 boolean 列不能跟整数比。

### 19.3.1 群消息的成员校验

发群消息前用 `IChatGroupMemberRepository.IsMemberAsync` 校验成员身份，否则谁都能往任意群Id发消息并扇出给全体成员。
校验排在建会话之前 —— 非成员不该留下任何痕迹。

### 19.4 仓储走 `DataAccessBase`

三个仓储（会话 / 消息 / 群成员）都继承 `Viv.Momo.Base.DataAccessBase`，与 Apex 的七个仓储同一套形状。

**没有用 `DataAccessCacheBase<T>`**：缓存按 Bucket 单条命中，而消息是高频追加、成员只在连接时读一次 ——
两个都不构成 Cache-Aside 的收益，还要额外背成员变更时的失效逻辑。

---

## 20. 会话列表

`GET /api/chat/sessions?pageIndex=&pageSize=`

返回 `ReceiverType + TargetId + 最后消息时间 + 置顶/免打扰 + 已读水位 + 未读数`。
单聊不带名字头像（这个域没有用户资料表，客户端按 TargetId 自己取），群聊带群名与头像。

### 20.1 数据来源分两侧

| 会话类型 | 来源 | 为什么 |
|---|---|---|
| 单聊 | `EtChatSession` 里**自己那一行** | 单聊的「最后消息时间」是每人视角，天然一人一行 |
| 群聊 | `EtGroupChatMember`（自己的成员行）+ `EtGroupChat` | 群聊的「最后消息时间」是**群级属性**，所有成员共用；每人只多一份置顶/免打扰 |

### 20.2 单聊发送时要给接收方也写一行

接收方没有自己的会话行，会话列表里就看不到这条会话。所以单聊发送现在是：

```
建/取发送方 session → 落库 → touch 发送方
                     → 建/取接收方 session → touch 接收方
```

群聊不这么做 —— 否则一条群消息要给每个成员各写一行，500 人群就是 500 次写。

### 20.3 未读数 + 标已读

`POST /api/chat/read { receiverType, targetId, lastReadMessageId }` 推进水位，写两个不同的地方：

| 会话类型 | 写哪 | 只前进不后退 |
|---|---|---|
| 单聊 | 自己那行 `EtChatSession.LastReadMsgId` | 旧请求乱序到达时不能把未读变回来 |
| 群聊 | 自己的成员行 `EtGroupChatMember.LastReadMsgId` | 同上；不是成员就什么都不做，不插行 |

未读 = `消息 Id > 已读水位` 且「发给我」的条数（群聊排除自己发的）。

**只在当前页算**（≤ pageSize 次 count）：全量会话各来一次 count 就是 N+1。
真要收敛成一条 SQL 得写带 join 的分组查询，等有压测数据再说 —— 现在的形状是「正确但每页多 N 次查询」。

---

## 21. 建群

`POST /api/chat/groups { name, avatarUrl, memberUserIds }` → `{ groupId }`

一个事务里写三处（`[VivUnitOfWork]`）：

1. `EtGroupChat`：群名、群主（`OwnerUserId` 取登录身份）、`MaxMemberCount`、`CreateAt`
2. `EtGroupChatMember`：群主一行 `MemberRole = 2`，其余成员各一行 `MemberRole = 0`
3. 群主自己的 `EtChatSession` 行（`ChatSessionType = 1`），否则这个群不出现在他的会话列表里

约定：

- 群主由登录身份决定，**请求里的 `memberUserIds` 只算其他成员**
- 成员列表去重，`<= 0` 与自己都会被排除
- 上限 500（含群主），超了直接拒
- `MemberRole`：0 普通成员、1 管理员、2 群主

### 21.1 成员变更的实时同步

| 接口 | 作用 |
|---|---|
| `POST /api/chat/groups/members/add` | 拉人进群 |
| `POST /api/chat/groups/members/remove` | 退群（自己）/ 踢人（群主、管理员） |
| `GET /api/chat/groups/members` | 群成员列表 |

权限：群主与管理员可以直接拉人；普通成员取决于群的 `AllowMemberInvite`。踢人需要群主或管理员，退自己不需要。
群主不能退群（群会失去归属）。

写库后在同一事务里投 `MembershipChangedEvent { TenantId, GroupId, UserIds, ChangeType }`：

```
接口 → 改 EtGroupChatMember → 发件箱入队（同事务）→ MQ fanout
      → 每个 Link 副本各自：pool.GetConnectionIds(tenant, user)
        → Joined 走 AddToGroupAsync / Left 走 RemoveFromGroupAsync
```

`MembershipChangedConsumer` 与 `SendMessageConsumer` 一样是 `Topology = PerInstance` 且关掉消费锁：
每个副本都要收到，各管自己那几条连接。

这样「拉进群立刻能收群消息、退群立刻收不到」不依赖客户端重连。

---

## 22. 撤回与指标

### 22.1 撤回

`POST /api/chat/messages/recall { receiverType, targetId, messageId }`

校验顺序：消息存在 → 是本人发的 → 对端与请求一致 → 未撤回 → 未超时限（2 分钟）。
写 `IsRecall = 1` / `RecallAt` 后投 `MessageRecalledEvent`，Link 侧下发 `MessageRecalled`（载荷 `HertaLinkRecall`）：
群聊推给群组，单聊推给对端的本机连接。

已撤回的请求幂等返回成功，不重复写、不重复发事件。

拉取的 `MessageItemOutput` 带 `IsRecall` / `RecallAt`，客户端据此显示占位而不是原文。

### 22.2 指标

`Meter("Viv.Kinna")`（原 `Viv.Herta.Link`，抽到框架层后改名），`AddMeter` 已加进 `Viv.Aspire.ServiceDefaults`（那份名单是手写死的，漏了就上不了 OTLP）。指标都带 `hub` 标签。

| 指标 | 类型 | 标签 |
|---|---|---|
| `kinna.connections` | UpDownCounter | 本实例活跃连接，进出配对记 |
| `kinna.dispatch` | Counter | `kind` = user / group / recall，`result` = ok / skipped / failed |
| `kinna.identity.override` | Counter | 事件署名与调用者不一致。代发、机器人会出现，不是异常 |

`skipped` 是「目标不在本实例」，每个副本都会大量产生，属于正常值。

---

## 23. 在线态

`GET /api/chat/presence?userIds=1,2,3` → `[{ userId, online }]`，`online` 为 `null` 表示查询失败、状态未知。

### 23.1 为什么必须跨副本共享

一个用户的连接可能在任何一台 Link 上，而每台只知道自己那几条。纯事件广播（连接建立/断开时发事件）会错：
A 的两个连接分在两台，其中一台断开就误报离线；丢一条事件就永久不准且没有自愈。

所以在线态存 Redis，形状是「一个键 + 连接计数 + TTL」：

```
viv:herta:presence:{tenant}:{user}  →  整数计数，TTL 90s
```

| 时机 | 操作 |
|---|---|
| 连接建立 | `INCR` + `EXPIRE 90s` |
| 断开 | `DECR`，减到 0 直接删键 |
| `Ping`（客户端 20s 一次） | `EXPIRE 90s` 续期 |
| 查询 | 有值且 > 0 即在线，键不存在即离线 |

多端天然正确（两个连接就是 2），多副本天然一致（所有副本操作同一个键），
**进程被 kill 时没人 DECR 也无所谓 —— TTL 到期自动消失**，这是它比事件方案强的地方。

### 23.2 Redis 不可用时降级

`PresenceService` 把所有 Redis 异常吞掉记 Warning，查询返回 `null`（未知）而不是 `false`。
`OnlineAsync` / `OfflineAsync` 失败也不影响连接建立与断开 —— 在线态只是展示信息。

这和「移除 Redis 背板」的原则一致：**Redis 只碰状态，不进投递路径**。

### 23.3 为之新增的框架能力

`IRedisService` 原来没有自增，加了：

```csharp
long Increment(string key, long value = 1);
Task<long> IncrementAsync(string key, long value = 1);
```

`value` 允许为负，递减传 `-1`。

---

## 24. IM 自有账号体系

IM 是独立第三方，**不认平台用户**。`EtChatUser.Id`（下文 `ChatUserId`）才是 IM 的身份，
与平台的 tenant/user 是两个命名空间。

### 24.1 注册与登录

| 接口 | 说明 |
|---|---|
| `POST /api/account/register` | `{ tenantId, appId, loginCode, password, nickName }` → `{ chatUserId }` |
| `POST /api/account/hertaLogin` | `{ tenantId, appId, loginCode, password }` → `{ token, expiresAt, chatUserId, nickName, avatar }` |

密码用 PBKDF2-SHA256（10 万次迭代、每用户 16 字节独立盐），`EtChatUser.Password` 存哈希、`Salt` 存盐，不存明文。

登录失败时「账号不存在」与「密码错误」返回同一句话，避免探出哪些账号存在。

### 24.2 身份如何进入 `IVivContext`

IM 自己签发 JWT，claims 与平台 token 同形（`sub` = ChatUserId、`AppId`、`SubjectId` = TenantId），
所以 `IVivContext.UserId` 直接就是 ChatUserId，**下游一行都不用改**。

身份来源优先级：

1. `x-viv-*` 头（经网关）
2. 没有头时回落 JWT —— `DefaultVivContextProvider` 本来就实现了这条，HTTP 侧零改动
3. `ChatHub` 原来只读头，本次补了 JWT 回落（客户端直连 Link 时没有网关头）

### 24.3 密钥必须与平台不同

`TokenOption` 换成 IM 专属的一组（`viv.herta`），**不能复用平台的 `viv.system.net` 那套**：
两边密钥相同的话平台 token 也能在 IM 上验过，平台 userId 会被当成 ChatUserId 写进会话与消息表，
两个命名空间就串了。换过之后平台 token 在 IM 上验签失败，隔离由密码学保证。

前提：`Viv.Herta.Api` 与 `Viv.Herta.Link` 的 `TokenOption` 必须完全一致（同一份密钥、Issuer、Audience）。

### 24.4 还没做

- 改密码 / 找回密码、刷新 token（现在 token 过期要重新登录）
- 用户资料（昵称、头像、个性签名）的修改接口
- 好友关系、黑名单、敏感词 —— `EtChatFriendApply` / `EtChatBlack` / `EtChatSensitiveWord` 三张表仍是空的

---

## 附录 A：为什么不用 Redis 背板（B 路线）

记录结论，便于日后回看为什么没选它。

| 维度 | A：MQ 实例广播 | B：单队列 + Redis 背板 |
|---|---|---|
| 投递跳数 | 1（MQ → 实例 → 连接） | 2（MQ → 实例 → Redis → 归属实例 → 连接） |
| 单连接有序性 | ✅ 队列 FIFO（配合单监听 + 单并行） | ❌ 竞争消费导致跨实例乱序 |
| Redis 故障 | 实时链路不受影响 | 实时投递**全停** |
| 扩容 | 流量 ×N | O(1) |
| 依赖面 | 只依赖 MQ | MQ + Redis |
| 代码量 | 队列属性 + 关锁 + 移除背板 | `IUserIdProvider` + 寻址 + 连接注册表 |

否决 B 的三个理由，按权重：
1. **有序性**：B 的乱序是结构性的（消息 1 在副本 A、消息 2 在副本 B，谁先写进 Redis 不定），聊天场景不可接受；修它要么给用户加分布式锁（吞吐打回单线程），要么单消费者（吞吐砍掉）
2. **故障面**：B 把 Redis 从「缓存」提升为「实时链路关键路径」，而当前 `AbortOnConnectFail: true`，Redis 一抖 IM 实时全停
3. **代码量**：A 反而更少 —— 不需要 `IUserIdProvider`、不需要注册表参与投递、不需要论证背板订阅时序

## 附录 B：什么时候迁到 B2（按用户分片）

触发条件（满足任一）：

- 副本数稳定 > 20
- 单条消息体积大，N 倍带宽成为瓶颈
- 需要按用户做**亲和调度**（如会话状态放本地内存）

做法要点：

- 分片数固定（如 16），队列名 `{EventName}Queue.{ServiceName}.shard{n}`
- 分片键 = `hash(tenantId, userId) % n`，**同一用户永远同分片** → 保序
- 发布侧需要路由到具体分片（RabbitMQ direct/topic 交换机，或发布到分片专属交换机）
- 消费侧按分片订阅，每分片单监听 + 单并行

**B2 不是 B**：它保住了有序性，也拿到了扩展性；代价是发布侧要知道分片规则，比 A 复杂。A 是它的「副本数少」特例。

---

## 25. 通用能力抽取到 `Viv.Kinna`（P1 已落地）

> 范围：只搬「与聊天语义无关」的那部分，**行为零变化**。消息体、会话、已读、群管理仍在 `Herta.Core`。

### 25.1 移走的

| 原位置（`Viv.Herta.Link`） | 新位置（`Viv.Kinna`） | 改名 |
|---|---|---|
| `Hubs/ConnectionPool.cs` + `IConnectionPool` | `Connections/KinnaConnectionPool.cs` + `IKinnaConnectionPool` | `ConnectionInfo` → `KinnaConnectionInfo` |
| `Hubs/ConnectionCoordinator.cs` + `IConnectionCoordinator` | `Connections/KinnaConnectionCoordinator.cs` + `IKinnaConnectionCoordinator` | 新增 `KickAsync` / `KickUserAsync` / `KickSubjectAsync` |
| `Hubs/ConnectionLeaseSweeper.cs` | `Connections/KinnaConnectionLeaseSweeper.cs` | — |
| `Hubs/HertaLinkClientMethods.cs` | `Contracts/KinnaClientMethods.cs` | — |
| `Contracts/HertaLinkKeys.cs`（+ `HertaLinkGroups`） | `Contracts/IKinnaKeys` + `KinnaKeys` | 前缀来自 `KinnaOptions.Prefix`，不再是静态 `"herta"` |
| `Contracts/HertaLinkConnected/Pong/ResyncResult` | `Contracts/KinnaConnected/KinnaPong/KinnaResyncResult` | 都继承 `KinnaPayload`（`V` 统一到基类） |
| `Metrics/HertaLinkMetrics.cs` | `Metrics/KinnaMetrics.cs` | Meter `Viv.Herta.Link` → **`Viv.Kinna`**，指标名 `herta.link.*` → `kinna.*` |
| `Options/HertaLinkOptions.cs` | `Options/KinnaOptions.cs` | 4 项全通用，原类删除；`appsettings.json` 新增 `KinnaOptions` 节点 |

### 25.2 留在 Herta 的（判据：与聊天语义 / Herta 事件契约绑定）

`ChatHub`（身份解析 8 行，依赖 `Viv.Engine.Power`）、`HertaLinkMessage` / `HertaLinkRecall`（`Em*` 枚举）、`IFanoutPlanner` / `FanoutPlanner`（入参是 `SendMessageEvent`）、`IMessageDispatcher` / `SignalRMessageDispatcher`（造 `HertaLinkMessage` 与 `HertaMagic` 多态体）、`MembershipSync`（入参是 `MembershipChangedEvent`）、3 个 `Consumer`、两个端口适配器 `Adapters/HertaGroupSource` / `HertaPresenceSink`。

### 25.3 新增的两个端口（框架不认识的域数据）

```csharp
IKinnaGroupSource   // GetUserGroupIdsAsync(subjectId, userId) —— 成员权威数据在 Herta.Core
IKinnaPresenceSink  // Online/Offline/Renew —— 可选，默认 Noop，不进投递路径
```

两个都是**可选构造参数**（Autofac 回落默认值）。不注册群端口时连接照常建立，只是群消息投不到人 —— 比「静默返回空列表」更好的选择是不假装。

### 25.4 关键改动：`IHubContext<THub>` 不再硬编码

`IHubContext<T>` 只能按具体类型解析，所以：

```
AddKinna<ChatHub>()  →  全进程唯一一次把 KinnaClientSender<THub> 闭合到 ChatHub
其余所有代码          →  只依赖 IKinnaClientSender（发连接 / 发多条 / 发组 / 加组 / 退组）
MapKinnaHub<ChatHub>() → 读 KinnaOptions.HubPath
```

`KinnaClientSender<THub>` 是 `internal`，只对 `Viv.Kinna.Tests` 开 `InternalsVisibleTo` —— 业务看到具体类型就会绕过接口。

### 25.5 依赖方向的硬约束

`Viv.Kinna` 只引 `FrameworkReference Microsoft.AspNetCore.App` + `Viv.Contracts` + `Viv.Log`。**刻意不引**：`Viv.Engine`（身份解析留在业务 Hub）、`Viv.Nana`（框架不认识 MQ，`KinnaPushOutcome` 那类返回值由业务映射）、`Viv.Redis`（A 路线零 Redis）。

顺带的收益：`Viv.Fakes` 不再引用 `Viv.Herta.Link`，信号替身改成泛型 `FakeHubContext<THub>`，另加 `RecordingKinnaClientSender` 等（`Kinna.cs`）。

### 25.7 一个进程托管多个 Hub（后续修正）

第一版把连接表 / 发送器 / 键注册成**无标签单例**，`AddKinna<THub>()` 里用 `TryAdd` —— 第二个 Hub 的注册被静默忽略。症状是「两个 Hub 共用一张连接表，A 的 connectionId 被 B 的 sender 去发」，SignalR 侧只留一条查无此连接的日志，编译期与运行期都不报错。

改成「每个 Hub 一套 Session」之后：

```csharp
services.AddKinna<ChatHub>(configuration, o => { o.HubPath = "/chat";  o.Prefix = "herta"; });
services.AddKinna<AdminHub>(configuration, o => { o.HubPath = "/admin"; o.Prefix = "admin"; });
```

| 能力 | 多 Hub 下的形状 |
|---|---|
| 连接表 / 发送器 / 键 / 配置 | 每个 Hub 一个 `IKinnaSession<THub>`，业务注入 `IKinnaSession<自己的Hub>` |
| 协调器 | `IKinnaConnectionCoordinator<THub>`（Scoped） |
| 租约清扫 | `KinnaConnectionLeaseSweeper<THub>`，每个 Hub 一个宿主服务 |
| 两个端口 | `IKinnaGroupSource<THub>` / `IKinnaPresenceSink<THub>`（多 Hub 的数据源本来就是两套） |
| 配置 | 先读共享 `KinnaOptions`，再读 `KinnaOptions:<Hub 类型名>` 覆盖，最后 `configure` 回调覆盖 |
| 指标 | 全部带 `hub` 标签，否则分不清是哪一路 |

`Viv.Kinna.Tests/KinnaMultiHubTests` 用真容器的 `ServiceCollection` 钉住这条：两个 Hub 的池互不可见、配置各取各的、各有一个清扫循环。

### 25.6 投递流程已下沉（原 P2，已完成）

`IKinnaPusher<TEvent>` + `KinnaMessageDispatcher<THub, TEvent>` 落地后，四件事从 Herta 收进了框架：

| 规则 | 现在的位置 |
|---|---|
| 目标不在本机 = `Skipped`，调用方按成功处理 | `KinnaMessageDispatcher` |
| 发送异常 = `Failed`，不重投 | 同上 |
| 署名与调用者不一致 = 只记审计 + 指标，照常投递 | 同上 |
| 计划非法（缺租户/群名）= `Dropped` | 同上 |

Herta 只剩两个推送计划：`ChatMessagePusher`（造 `HertaLinkMessage` + 选目标）、`ChatRecallPusher`。`IFanoutPlanner` / `FanoutDecision` / `IMessageDispatcher` / `SignalRMessageDispatcher` 全部删掉，`DispatchAsync` 与 `DispatchRecallAsync` 之间那份重复也消失了。

消费者现在只做两件事：把信封转成 `KinnaPushContext`，把 `KinnaPushOutcome` 映射回 `SubscribeResult`（`Success`/`Skipped` → 成功）。

**还没做**：

- **P3**：接一个**非 IM 域**验证通用性 —— 新域只写 `Hub + Pusher + Consumer + AddKinna<THub>()`，Kinna 一行不改。
- **P4**：上行限流、`MaxConnectionsPerUser`、presence 进协议、按 userId 分片队列。
- `MembershipSync` 的入参仍绑 `MembershipChangedEvent`，泛化成 `KinnaMembershipChange` 也还没做。
- 一处**遗留**：`KinnaConnectionPool.GetConnectionIds(subjectId, userId)` 仍是全表遍历（穷举 `_userConnections`），优化单独做。

---

## 26. 代发（以他人名义发消息）

### 26.1 规则

| 入口 | 署名 | 租户 | 是否落库 |
|---|---|---|---|
| HTTP `POST /api/chat/sendMessage` | `FromUserId > 0` 用它，否则 `IVivContext.UserId` | 固定 `IVivContext.SubjectId`，请求体没有租户字段 | 是 |
| MQ `SendMessageEvent` | `evt.FromUserId`（0 = 系统消息） | `evt.TenantId > 0` 用它，否则信封 `Context.SubjectId` | 否 |

一句话：**租户只在事件里体现**。HTTP 请求不开放租户，它永远写到登录身份自己的租户下；要跨租户只能直接发事件。

署名与调用者不一致时照常投递，记一条 `以他人名义发送` 的 Info，并累加 `kinna.identity.override`。

### 26.2 为什么 HTTP 不放租户

`TenantDataFilter` 按 `IVivContext.SubjectId` 过滤所有 `ITenant` 实体的查询。如果请求体能指定别的租户，发送路径上的会话查询、群成员校验会被过滤器挡在登录租户外面 —— 不报错，只是"查不到"，于是每条消息都新建一行会话，数据慢慢烂掉。要修就得在发送路径上关过滤器，再逐个接口审一遍。

代价和收益不成比例：跨租户代发是低频运营动作，直接发事件就够了。

### 26.3 跨租户代发的已知缺口

事件侧跨租户只负责**实时推送**，不写库：

- 消息不会出现在对方的会话列表和历史拉取里（`GET /api/chat/messages` 查不到）；
- 客户端断线期间这条消息就没了，补拉也补不回来。

如果运营需要"某个租户的历史里真的有这条消息"，那就得走 HTTP（同租户），或者补一条"代写入库"的服务端流程 —— 目前没做。

### 26.4 谁能连、谁能代发

两个问题的答案不在同一个地方：

| 问题 | 依据 |
|---|---|
| 这个客户端能不能连 Hub | 问 Apex：`AtClientApp` 里存在且 `Status = Enabled`（见 §27） |
| 这个请求能不能代发 | 没有限制，能调 `sendMessage` 就允许带 `FromUserId` |

要把代发也收紧，可以复用同一份 Apex 校验：ChatService 拿到 `IClientAppQuery`，`AppId` 不是"允许代发"的客户端就忽略请求里的 `FromUserId`。这一步还没做。

---

## 27. 连接准入：AppId 问 Apex，不写配置文件

### 27.1 为什么

客户端 AppId 的权威数据在 Apex 的 `AtClientApp`（`Id` 就是 AppId，`Status` 是 `EmStatus`）。在 IM 这边抄一份白名单意味着：Apex 新增客户端要改两处、禁用客户端要重启 Link，而且两边不同步时**不报错**，只是有人连不上或该拒绝的没拒绝。

### 27.2 放在哪

整套东西都在 `Viv.ServiceProxy`（业务侧 gRPC 层），不放 Herta：AppId 是平台级概念，DeepRed、Ouroboros 以后要用同一份判定，不该各写一遍。

| 件 | 位置 |
|---|---|
| 契约 | `Viv.ServiceProxy/Protos/apex_app.proto` |
| 端口 | `Viv.ServiceProxy/Apex/IClientAppQuery` + `ClientAppCheck` |
| 客户端实现 | `Viv.ServiceProxy/Apex/ApexClientAppQuery`（gRPC + 缓存） |
| 缓存 | `Viv.ServiceProxy/Apex/ClientAppCheckCache`（单例） |
| 注册 | `Viv.ServiceProxy/Apex/ApexClientAppExtensions.AddApexClientAppQuery(configuration)` |
| 服务端 | `Viv.Apex.Api/Grpc/ApexAppQueryService`，查 `IClientAppRepository.GetAppAsync(appId)`，由 `VivGrpcDiscovery` 自动注册映射 |

Herta 侧只剩一句 `services.AddApexClientAppQuery(configuration)` 和一个 `HertaLinkAdmission`。

```proto
service ApexAppQuery {
  rpc ValidateClientApp (ValidateClientAppRequest) returns (ValidateClientAppReply);
}
// reply: exists / enabled / name / message（不可用原因，直接进日志）
```

### 27.3 生命周期：查询 Scoped，缓存 Singleton

`IClientAppQuery` 是 Scoped 的。gRPC 客户端带的 `VivGrpcInterceptor` 要读当前请求的 `IVivContext`，注册成单例会把那份上下文捕获在根作用域上。

缓存是另一回事，必须跨连接共用，所以单独一个 Singleton 的 `ClientAppCheckCache` 注入进 Scoped 查询里。

### 27.4 缓存与失败策略

每条连接都问一次 gRPC 太重，结果在进程内缓存 `CacheSeconds`（默认 300 秒，正负结果都缓存）。代价是 Apex 禁用某个客户端后，最多 300 秒才在 Link 侧生效。

Apex 不可达时**不放行**：返回「客户端校验服务不可用」并记 Error。宁可连不上，也不要让未知客户端混进来。

### 27.5 配置

```jsonc
"ApexClientAppOption": {
  "ServiceName": "viv-apex-api",   // 服务发现填服务名，直连填完整地址
  "UseServiceDiscovery": true,
  "CacheSeconds": 300
}
```

`AppHost` 里 `viv-herta-link` 必须 `WithReference(apexApi)`，否则拿不到 `services__viv-apex-api` 环境变量。本地直连可以把 `UseServiceDiscovery` 关掉、`ServiceName` 填 `http://localhost:7001`。

另外 `VivOptions.EchoOption.GrpcOption` 要给一个非 null 值（`EnableServer: false` 即可）：`VivGrpcInterceptor` 只在 `GrpcOption != null` 时注册，没有它客户端发不出带租户头的请求。



