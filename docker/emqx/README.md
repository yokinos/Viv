# EMQX（设备接入）

框架侧的 MQTT 机制在 `src/Banshee/Viv.Nana/Mqtt`，配置节点是 `VivOptions.MqttOption`（为 null = 不启用）。
这里只放 broker 与访问控制。

## 起服务

```bash
cd docker && docker compose up -d emqx
```

| 端口 | 用途 |
|---|---|
| 1883 | MQTT（设备与平台都用这个） |
| 8883 | MQTT over TLS（生产走这个） |
| 8083 | MQTT over WebSocket |
| 18083 | Dashboard：http://localhost:18083 ，账号 `admin` / `viv_emqx_77`（compose 里改的） |

## 授权（已配好，需确认）

`acl.conf` 的核心是一条：**设备的 clientId 必须等于 machineId，它就只能读写 `dev/{machineId}/#`**。

compose 里用环境变量挂了 File 数据源。若启动后发现规则没生效，去 Dashboard →
访问控制 → 授权 → 数据源，手动加一条 File 源指向 `/opt/emqx/etc/acl.conf`，并把 `no_match` 设为 `deny`。

## 认证（还没配，上线前必须做）

EMQX 默认**不校验凭据**，也就是任何客户端都能随便连 —— 在授权规则生效前，设备可以冒用别人的 clientId。
推荐两种做法：

1. **内置数据库**：Dashboard → 访问控制 → 认证 → 新建 `Password-Based` → `Built-in Database`，
   然后逐台设备导入 `clientid / username / password`（或用 EMQX 的 API 批量导入，与设备档案表对齐）。
2. **HTTP 认证**：认证源指向业务侧的接口，由业务查机器档案判断这台设备是否合法 ——
   设备量级小（每机构十几台），但设备会换、会报废，走业务接口更容易保持一致。

## 平台自己也要过 ACL

设备规则是「clientId 只能碰 `dev/{自己的machineId}/#`」，平台要反过来订阅**所有**设备的上行，
所以 `acl.conf` 里给平台单独开了一条，走 username 而不是 clientId 前缀（clientId 客户端可随便填）：

```erlang
{allow, {username, "viv-platform"}, all, ["dev/#"]}.
```

配套的配置侧要求：

- 平台服务的 `VivOptions.MqttOption.UserName` 必须是 `viv-platform`（EMQX 认证里也要有这条身份）；
- 平台的 `ClientId` 保持 `viv-...` 这种自动生成值即可，不参与授权判断。

## 上线前清单

- [ ] 认证已开，匿名连接已关
- [ ] Dashboard 默认密码已改（不是 `public`）
- [ ] 设备走 8883（TLS），1883 只对本机/内网开放
- [ ] 每台设备的 clientId 就是 machineId，且 ACL 的 `%c` 隔离已验证
- [ ] 平台以 `viv-platform` 身份接入，且它订阅 `dev/+/up/#` **不被 ACL 拦**（这条最容易漏）
