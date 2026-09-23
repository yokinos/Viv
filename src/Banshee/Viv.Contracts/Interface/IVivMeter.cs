using System.Collections.Generic;
using Viv.Contracts.Models;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 进程内指标的读取与清零，给管理后台用。
    ///
    /// System.Diagnostics.Metrics 是只写的 —— 仪表把测量值推给监听者，producer 自己读不回来，
    /// 也没有清零入口。想让运维「出故障了，从现在开始重新数」，只能自己在读侧收一份账。
    /// 框架因此常驻一个 MeterListener，按 Viv. 前缀收全部框架指标，本接口读的就是这份账。
    ///
    /// 三件事先讲清楚，免得把数读歪：
    ///
    /// ① 清零只清框架自持的这一份。Aspire / Grafana 那侧走 OpenTelemetry 导出，那边没有公开的
    /// 清零入口，面板上还是连续的累计值 —— 「清零了为什么 Grafana 没归零」不是 bug。
    ///
    /// ② 统计口径是「自组装起（AddViv 那一刻）」，不是进程生命周期，也不是某个时间窗口。
    ///
    /// ③ 这是当前进程的视图。多副本部署时每个实例各有一份，跨实例汇总仍归 OTel 面板管。
    /// 读到的是单副本的数，别当成全局。
    /// </summary>
    public interface IVivMeter
    {
        /// <summary>
        /// 已经收上账的 meter 名，去重且已排序。
        ///
        /// 空集合说明 listener 没挂上（或这次组装没走 AddViv），是排查「数字怎么全是空的」时
        /// 第一个该看的地方 —— 它不依赖有没有测量发生过，只要有仪表被 publish 过就有值。
        /// </summary>
        IReadOnlyList<string> Meters { get; }

        /// <summary>
        /// 当前读数快照，一行一个「仪表 × 标签组合」。
        ///
        /// 顺带会把水位类仪表（Gauge）拉新一次 —— 它们不会主动推数，不拉就只能读到上一次的值。
        /// 标签不同就是不同的行，各自独立累计。
        ///
        /// 这是诊断读：内部逐个仪表加锁，别放进每请求路径。
        /// </summary>
        IReadOnlyList<VivMeterSnapshot> Snapshot();

        /// <summary>
        /// 清零并重新计数，返回清零前那一刻的快照，便于调用方留痕。
        ///
        /// 返回的是清零**之前**的值，所以水位类仪表在这里是当前水位而不是 0 —— 它没撒谎，
        /// 下一次 Snapshot 会重新拉一次水的。
        ///
        /// 与测量比邻的几条可能正好落在「读完快照、还没清空」的窗口里被一并清掉，
        /// 这是清零这个语义本身带的，不是缺陷。
        /// </summary>
        IReadOnlyList<VivMeterSnapshot> Reset();
    }
}
