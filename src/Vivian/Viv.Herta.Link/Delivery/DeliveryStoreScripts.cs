namespace Viv.Herta.Link.Delivery
{
    /// <summary>
    /// 投递确认的两段 Lua。合并在脚本里做，是为了「读水位 → 合并 → 截断 → 写」之间不被别的
    /// 连接插进来：多端同时 Ack 时，读-改-写分开做会把对方刚写的明细覆盖掉。
    ///
    /// Hash 字段：w = 已确认到的位置，ids = 逗号分隔的 Id 列表（超限时清空只留 w）。
    /// </summary>
    internal static class DeliveryStoreScripts
    {
        /// <summary>ARGV：limit、ttl 秒、以及本次确认的 Id；返回合并后的位置。</summary>
        public const string Ack = """
            local limit = tonumber(ARGV[1])
            local ttl = tonumber(ARGV[2])
            local w = tonumber(redis.call('HGET', KEYS[1], 'w') or '0') or 0
            local raw = redis.call('HGET', KEYS[1], 'ids') or ''
            local set = {}
            for id in string.gmatch(raw, '%d+') do
              local n = tonumber(id)
              if n and n > 0 then set[n] = true end
            end
            for i = 3, #ARGV do
              local n = tonumber(ARGV[i])
              if n and n > 0 then
                set[n] = true
                if n > w then w = n end
              end
            end
            local count = 0
            for _ in pairs(set) do count = count + 1 end
            local out = {}
            if limit > 0 and count <= limit then
              for n in pairs(set) do out[#out + 1] = tostring(n) end
            end
            redis.call('HSET', KEYS[1], 'w', w)
            redis.call('HSET', KEYS[1], 'ids', table.concat(out, ','))
            redis.call('EXPIRE', KEYS[1], ttl)
            return w
            """;

        /// <summary>返回 {w, ids} 两个原始值，不解 JSON 也不反序列化。</summary>
        public const string Read = """
            return { redis.call('HGET', KEYS[1], 'w') or '0', redis.call('HGET', KEYS[1], 'ids') or '' }
            """;
    }
}
