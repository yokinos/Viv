namespace Viv.Herta.Link.Delivery
{
    /// <summary>
    /// 已确认 Id 列表的编解码。Redis 里只存一个字段，值形如 "11,12,13" ——
    /// 一个字段是为了让一次 Ack 只写一次，而不是每条 Id 一次 HSET。
    /// Lua 脚本里用的也是同一套标点规则，改这里要同步改脚本。
    /// </summary>
    public static class DeliveryIds
    {
        public static long[] Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return [];

            var ids = new List<long>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out var id) && id > 0)
                    ids.Add(id);
            }

            return [.. ids.Distinct().Order()];
        }

        public static string Format(IEnumerable<long> ids)
            => string.Join(',', ids.Where(x => x > 0).Distinct().Order());
    }
}
