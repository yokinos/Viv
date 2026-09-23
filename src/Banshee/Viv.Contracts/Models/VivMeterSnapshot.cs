using System.Collections.Generic;
using Viv.Contracts.Enums;

namespace Viv.Contracts.Models
{
    /// <summary>
    /// 一条读数序列，即「某个仪表 × 某组标签」的全部累计。
    ///
    /// 快照是一维列表而不是按 meter / instrument 分层的树 —— 管理后台多半直接铺成表格，
    /// 分层只对画树形 UI 有好处。
    /// </summary>
    public class VivMeterSnapshot
    {
        /// <summary>
        /// meter 名，如 Viv.Redis
        /// </summary>
        public string Meter { get; set; } = string.Empty;

        /// <summary>
        /// 仪表名，如 viv.redis.command.duration
        /// </summary>
        public string Instrument { get; set; } = string.Empty;

        /// <summary>
        /// 读数语义，决定 Count / Sum / Min / Max 怎么读
        /// </summary>
        public VivMeterKind Kind { get; set; }

        /// <summary>
        /// 计量单位，如 ms；建仪表时没给就是 null
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// 这组读数挂的标签。同一个仪表下标签不同就是不同的序列，各自独立累计
        /// </summary>
        public IReadOnlyDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 测量次数。Gauge 恒为 1
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// 合计值。Gauge 是最新水位
        /// </summary>
        public double Sum { get; set; }

        /// <summary>
        /// 最小值。Gauge 与 Sum 相同
        /// </summary>
        public double Min { get; set; }

        /// <summary>
        /// 最大值。Gauge 与 Sum 相同
        /// </summary>
        public double Max { get; set; }
    }
}
