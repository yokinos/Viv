namespace Viv.Herta.Core.Mapping
{
    /// <summary>
    /// 库里的时间列存的是 UTC，但往返后 Kind 可能是 Unspecified。直接 new DateTimeOffset 会被
    /// 当成服务器本地时间，偏移跟着机器走 —— 所以统一先标回 UTC 再转。
    /// </summary>
    public static class UtcTime
    {
        public static DateTimeOffset FromUtc(DateTime value)
            => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        public static DateTimeOffset? FromUtc(DateTime? value)
            => value is { } v ? FromUtc(v) : null;
    }
}
