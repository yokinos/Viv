using System;

namespace Viv.Elysia.Request
{
    /// <summary>
    /// 日期区间（不可变值类型）
    /// </summary>
    public readonly struct DateRangeDefine : IEquatable<DateRangeDefine>
    {
        /// <summary>
        /// 起始时间（含）
        /// </summary>
        public DateTime Start { get; }

        /// <summary>
        /// 结束时间（含）
        /// </summary>
        public DateTime End { get; }

        public DateRangeDefine(DateTime start, DateTime end)
        {
            if (start > end)
            {
                throw new ArgumentException("开始时间不得大于结束时间", nameof(start));
            }

            Start = start;
            End = end;
        }

        /// <summary>
        /// 区间时长
        /// </summary>
        public TimeSpan Duration => End - Start;

        /// <summary>
        /// 校验区间合法性（用于序列化反序列化、外部输入等场景，因构造函数无法保证）
        /// </summary>
        /// <param name="maxDays">可选：最大允许天数（含当天边界，按自然日计算）</param>
        /// <returns>合法返回 null，否则返回错误信息</returns>
        public string? Validate(int? maxDays = null)
        {
            if (Start == default && End == default)
            {
                return "日期区间不能为空";
            }

            if (Start > End)
            {
                return "开始时间不得大于结束时间";
            }

            if (maxDays.HasValue)
            {
                if (maxDays.Value < 0)
                {
                    // 调用方传参错误，按编程错误抛出更合适，或返回 null 跳过校验
                    return "最大日期区间参数不能为负数";
                }

                // 用 TotalDays 避免 Days 的截断；日期区间一般按自然日算更符合直觉
                var days = (End.Date - Start.Date).Days + 1;
                if (days > maxDays.Value)
                {
                    return $"最大日期区间为 {maxDays.Value} 天";
                }
            }

            return null;
        }

        public bool Contains(DateTime value) => value >= Start && value <= End;

        public bool Contains(DateRangeDefine other) => other.Start >= Start && other.End <= End;

        public bool Overlaps(DateRangeDefine other) => Start <= other.End && other.Start <= End;

        public bool Equals(DateRangeDefine other) => Start == other.Start && End == other.End;

        public override bool Equals(object? obj) => obj is DateRangeDefine other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Start, End);

        public static bool operator ==(DateRangeDefine left, DateRangeDefine right) => left.Equals(right);

        public static bool operator !=(DateRangeDefine left, DateRangeDefine right) => !left.Equals(right);

        public override string ToString() => $"[{Start:yyyy-MM-dd HH:mm:ss} ~ {End:yyyy-MM-dd HH:mm:ss}]";
    }
}