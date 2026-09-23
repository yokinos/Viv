using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Engine.Metrics
{
    /// <summary>
    /// IVivMeter 的实现 —— 常驻一个 MeterListener，把框架自己发的 Viv.* 指标收成一份可读的账。
    ///
    /// 写侧一行没动：RedisMetrics / MomoMetrics / NanaMetrics / OutboxMetrics 还是照常往各自的
    /// Meter 上记，这里只是多挂了一个监听者。将来新加 meter，只要名字以 Viv. 开头就自动收进来，
    /// 不必改这个类。
    ///
    /// 指标有四个 Meter 但都叫 Viv.xxx，所以按前缀过滤，而不是维护一张 meter 名单 ——
    /// 名单那种写法会在新人加 meter 时静默漏收。
    ///
    /// 这个类不引 Viv.Redis / Viv.Momo 等具体项目，收账靠的是 listener 的泛化能力；
    /// 一旦在这里写死了某个 meter 常量，就退化成硬编码清单了。
    /// </summary>
    internal sealed class VivMeter : IVivMeter, IDisposable
    {
        private readonly string _meterPrefix;

        /// <summary>
        /// 必须由字段持有强引用 —— Meter 对监听者只持弱引用，被 GC 收走就静默不再收数
        /// </summary>
        private readonly MeterListener _listener;

        /// <summary>
        /// 只服务 Snapshot / Meters 的枚举与驱逐，热路径不碰它 —— 测量回调那边走
        /// EnableMeasurementEvents 塞进去的 state，省掉一次查表
        /// </summary>
        private readonly ConcurrentDictionary<Instrument, InstrumentState> _instruments = new();

        /// <param name="meterPrefix">
        /// 收哪些 meter。默认收框架自己的 Viv. 前缀；测试传别的前缀拿到一份互不干扰的账
        /// </param>
        internal VivMeter(string meterPrefix = "Viv.")
        {
            _meterPrefix = meterPrefix ?? throw new ArgumentNullException(nameof(meterPrefix));
            _listener = new MeterListener();

            // 回调必须先注册再 Start：listener 一启动就开始派发测量，晚注册的类型会被直接丢掉。
            // 七种数值类型全注册 —— 只注册 long / double 的话，将来有人建个 Counter<int>
            // 会静默收不到，正是本仓库反复踩的那种「编译通过、跑起来只是少数」
            Hook<double>(static value => value);
            Hook<float>(static value => value);
            Hook<long>(static value => value);
            Hook<int>(static value => value);
            Hook<short>(static value => value);
            Hook<byte>(static value => value);
            Hook<decimal>(static value => (double)value);

            _listener.InstrumentPublished = OnInstrumentPublished;
            _listener.MeasurementsCompleted = OnMeasurementsCompleted;
            _listener.Start();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Meters
        {
            get
            {
                var names = new List<string>();

                foreach (var instrument in _instruments.Keys)
                {
                    var name = instrument.Meter.Name;

                    if (!names.Contains(name))
                        names.Add(name);
                }

                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<VivMeterSnapshot> Snapshot()
        {
            RefreshObservable();

            var result = new List<VivMeterSnapshot>();

            foreach (var state in _instruments.Values)
                result.AddRange(state.Snapshot());

            // 字典的枚举顺序不稳定，这里排一下 —— 管理后台的表不该每次刷新都换个行序
            result.Sort(static (x, y) =>
            {
                var byMeter = string.CompareOrdinal(x.Meter, y.Meter);
                return byMeter != 0 ? byMeter : string.CompareOrdinal(x.Instrument, y.Instrument);
            });

            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<VivMeterSnapshot> Reset()
        {
            // 先读后清：返回的是清零前那一刻的值。水位类仪表在这一份里是当前水位而不是 0，
            // 下一次 Snapshot 会重新拉，所以不需要为它开特例
            var snapshot = Snapshot();

            foreach (var state in _instruments.Values)
                state.Clear();

            return snapshot;
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        /// <summary>
        /// 水位类仪表不会主动推数，得显式拉一次。回调跑在调用方线程上（管理后台的请求线程），
        /// 所以第一方的 gauge 回调要廉价且纯
        /// </summary>
        private void RefreshObservable()
        {
            _listener.RecordObservableInstruments();
        }

        private void Hook<T>(Func<T, double> convert) where T : struct
        {
            _listener.SetMeasurementEventCallback<T>((_, value, tags, state) =>
            {
                if (state is InstrumentState target)
                    target.Record(convert(value), tags);
            });
        }

        private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
        {
            if (!instrument.Meter.Name.StartsWith(_meterPrefix, StringComparison.Ordinal))
                return;

            // GetOrAdd 而不是直接赋值：同一个仪表被 publish 两次时不该把已收的账抹掉
            var state = _instruments.GetOrAdd(instrument, static i => new InstrumentState(i, KindOf(i)));

            // 塞进去的这个对象会原样回到测量回调的 state 参数上
            listener.EnableMeasurementEvents(instrument, state);
        }

        /// <summary>
        /// 仪表被释放后测量静默失效（Add 不抛、也没人收），留在字典里就是一条冻在最后一帧的
        /// 值 —— 管理后台会把它当成当前值展示，永远不动也永远不报错。这里直接摘掉，
        /// 让它从 Meters 和快照里消失，是个看得见的信号
        /// </summary>
        private void OnMeasurementsCompleted(Instrument instrument, object? state)
        {
            _instruments.TryRemove(instrument, out _);
        }

        /// <summary>
        /// Instrument 没有公开的类型标识，只能拿泛型定义比对。
        ///
        /// 只有 Gauge 一族算水位，其余（Counter / UpDownCounter / ObservableCounter /
        /// ObservableUpDownCounter）都按累计读 —— 与 OTel 对这几类的归类一致。未知类型也走累计，
        /// 因为累计是多数派，而且这里的未知只可能是将来新增的仪表类型
        /// </summary>
        private static VivMeterKind KindOf(Instrument instrument)
        {
            var type = instrument.GetType();
            var generic = type.IsGenericType ? type.GetGenericTypeDefinition() : null;

            if (generic == typeof(Histogram<>))
                return VivMeterKind.Histogram;

            if (generic == typeof(Gauge<>) || generic == typeof(ObservableGauge<>))
                return VivMeterKind.Gauge;

            return VivMeterKind.Counter;
        }

        /// <summary>
        /// 一个仪表下的全部序列。仪表名与本类的调用点一一对应，标签是低基数的枚举，
        /// 所以每个仪表下最多十几条序列
        /// </summary>
        private sealed class InstrumentState
        {
            private readonly List<Series> _series = new();

            public InstrumentState(Instrument instrument, VivMeterKind kind)
            {
                Instrument = instrument;
                Kind = kind;
            }

            public Instrument Instrument { get; }

            public VivMeterKind Kind { get; }

            /// <summary>
            /// 保护 _series 的锁。线上千级 QPS 摊到二十来个仪表上，一次无竞争取锁完全不算数，
            /// 换来的是不必为无锁结构操心内存序
            /// </summary>
            private object Sync { get; } = new();

            public void Record(double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                lock (Sync)
                {
                    var series = Match(tags);

                    if (series == null)
                    {
                        series = new Series(CopyTags(tags));
                        _series.Add(series);
                    }

                    // 水位问的是「现在是几」不是「一共多少」，后值覆盖前值
                    if (Kind == VivMeterKind.Gauge)
                    {
                        series.Count = 1;
                        series.Sum = value;
                        series.Min = value;
                        series.Max = value;
                        return;
                    }

                    series.Count++;
                    series.Sum += value;

                    if (value < series.Min)
                        series.Min = value;

                    if (value > series.Max)
                        series.Max = value;
                }
            }

            public List<VivMeterSnapshot> Snapshot()
            {
                lock (Sync)
                {
                    var result = new List<VivMeterSnapshot>(_series.Count);

                    foreach (var series in _series)
                        result.Add(series.ToSnapshot(Instrument, Kind));

                    return result;
                }
            }

            public void Clear()
            {
                lock (Sync)
                    _series.Clear();
            }

            /// <summary>
            /// 按标签键匹配，不按位置。按位置比的话就等于把「同一个仪表的每个调用点标签顺序必须一致」
            /// 变成一条隐式契约，哪天有人写成 {op, path} 就会静默多出一条各记一半的重复序列
            /// </summary>
            private Series? Match(ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                foreach (var series in _series)
                {
                    if (TagsMatch(series.Tags, tags))
                        return series;
                }

                return null;
            }

            private static bool TagsMatch(KeyValuePair<string, string?>[] existing, ReadOnlySpan<KeyValuePair<string, object?>> incoming)
            {
                if (existing.Length != incoming.Length)
                    return false;

                foreach (var tag in existing)
                {
                    var matched = false;

                    foreach (var candidate in incoming)
                    {
                        if (!string.Equals(candidate.Key, tag.Key, StringComparison.Ordinal))
                            continue;

                        if (!string.Equals(candidate.Value?.ToString(), tag.Value, StringComparison.Ordinal))
                            return false;

                        matched = true;
                        break;
                    }

                    if (!matched)
                        return false;
                }

                return true;
            }

            /// <summary>
            /// 只在建序列时拷一次。回调收到的 tags 是 span，出了回调就作废，不能留引用
            /// </summary>
            private static KeyValuePair<string, string?>[] CopyTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                var copy = new KeyValuePair<string, string?>[tags.Length];

                for (var i = 0; i < tags.Length; i++)
                    copy[i] = new KeyValuePair<string, string?>(tags[i].Key, tags[i].Value?.ToString());

                return copy;
            }
        }

        /// <summary>
        /// 一条序列的累计值。Min / Max 初值取正负无穷，第一次测量就会被顶掉
        /// </summary>
        private sealed class Series
        {
            public Series(KeyValuePair<string, string?>[] tags)
            {
                Tags = tags;
                Min = double.PositiveInfinity;
                Max = double.NegativeInfinity;
            }

            public KeyValuePair<string, string?>[] Tags { get; }

            public long Count { get; set; }

            public double Sum { get; set; }

            public double Min { get; set; }

            public double Max { get; set; }

            public VivMeterSnapshot ToSnapshot(Instrument instrument, VivMeterKind kind)
            {
                var tags = new Dictionary<string, string>(Tags.Length);

                foreach (var tag in Tags)
                    tags[tag.Key] = tag.Value ?? string.Empty;

                return new VivMeterSnapshot
                {
                    Meter = instrument.Meter.Name,
                    Instrument = instrument.Name,
                    Kind = kind,
                    Unit = instrument.Unit,
                    Tags = tags,
                    Count = Count,
                    Sum = Sum,
                    Min = Min,
                    Max = Max
                };
            }
        }
    }
}
