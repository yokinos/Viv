using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 配置版本闸门：让"改了库里的 Agent / 工具配置"跨实例生效。
    ///
    /// 进程内的缓存字典只清自己那台机器，多副本部署时其余实例要等 60 秒 TTL 才看到新配置，
    /// 所以 refresh 顺手把一个共享版本戳写进 Redis，其余实例节流读它、发现变了就清自己的缓存。
    ///
    /// 共享戳只覆盖"走管理接口"的变更。<b>裸改库</b>（直接 UPDATE 绑定表把某条能力改成需审批 + 白名单）
    /// 不写戳，所以实现还会节流比对一次库侧指纹（四张配置表的判定列），变形即视为配置变过。
    /// 这条兜底是审批闸门的一部分：没有它，"免审批"的工具列表会活到 60 秒 TTL，
    /// 这期间白名单内的主体调用需审批工具不会弹审批。
    /// </summary>
    public interface IConfigVersionGate
    {
        /// <summary>本进程当前已应用的配置版本；与上次不同就说明该清缓存</summary>
        long Generation { get; }

        /// <summary>节流地看一眼共享版本戳；发现别的实例 bump 过就自增本进程版本并返回新值</summary>
        long EnsureFresh();

        /// <summary>把"配置变了"广播出去（refresh 调用）；拿不到 Redis 时只自增本进程版本</summary>
        long Publish();
    }
}
