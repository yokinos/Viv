# docker/ — 本地依赖中间件

**这里不是部署编排。** 本目录的 `docker-compose.yml` 只负责把开发 / 联调要用的**依赖中间件**拉起来；
**应用本身（API / Worker / Gateway）由 Aspire AppHost 编排启动**，不进 compose：

```bash
dotnet run --project src/Vivian/Viv.Aspire/Viv.Aspire.AppHost
```

换句话说，`compose up` 之后能连上库、缓存、MQ，但**没有业务端口在监听** —— 这是设计如此，不是缺服务。
生产部署是另一套东西（容器镜像 + 编排），也不在本目录。

## compose 里的服务

| 服务 | 容器 | 端口 | 用途 |
|---|---|---|---|
| `seq` | `Viv_Seq` | 5341 | 结构化日志（`LogOption.SeqApiKey`） |
| `redis` | `Viv_Redis` | 6379 | 缓存 / 分布式锁 / Kinna 踢人广播 |
| `sqlserver` | `Viv_SQLServer` | 1433 | 本地 SQL Server 2022 |
| `rabbitmq` | `Viv_RabbitMQ` | 5672 / 15672 | Nana 跨进程消息（vhost `/viv`） |
| `emqx` | `Viv_EMQX` | 1883 / 8883 / 8083 / 18083 | MQTT（ACL 见 `emqx/acl.conf`） |
| `caddy` | `Viv-Caddy` | 80 / 443 | 见下 |

密码都是 `viv_*_77` 的开发口令，和 `appsettings.Development.json` 对得上；**不要带到生产**。

## Caddy 只做外网域名入口，不参与业务路由

`Caddyfile` 当前只有一条 `pgadmin.katoumegumi.net → viv_pgadmin:80`，给数据库管理界面挂域名。

**注意 `viv_pgadmin` 不在本 compose 里** —— 它是在宿主机上单独起的容器（`container_name` 那个名字），
Caddy 反代到同一 docker 网络里的容器名。容器不存在时 Caddy 照常启动，只是访问该域名 502。
业务流量的反向代理在 `Viv.Aspire.Gateway`（YARP，`/{短名}/api/**`），和这个 Caddy 没有任何关系。

## 已知缺口（不是 bug，是没配）

- compose 里**没有 PostgreSQL**，而 `DatabaseOptions.DatabaseSource` 默认是 PostgreSQL —— 本地跑默认配置要自己起一个 PG，或把 `DatabaseSource` 改成 `SqlServer`。
- 没有 pgadmin / 没有 rabbitmq 的 `definitions.json`，管理界面和队列都要手工建。
- Windows 下不用 compose 的，看 `docker.windows.txt` 里的 `docker run` 单条命令。
