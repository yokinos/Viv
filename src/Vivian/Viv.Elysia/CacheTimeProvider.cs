using System;
using Viv.Delusion.Magic;

namespace Viv.Elysia
{
    /// <summary>
    /// 缓存时间提供器
    /// 提供随机过期时间，防止缓存雪崩
    /// </summary>
    public class CacheTimeProvider
    {
        /// <summary>
        /// 获取随机天数的缓存时间
        /// </summary>
        /// <param name="minDays">最小天数</param>
        /// <param name="maxDays">最大天数</param>
        /// <returns>随机TimeSpan</returns>
        public static TimeSpan GetRandomDays(int minDays = 1, int maxDays = 30)
        {
            if (minDays < 1) minDays = 1;
            if (maxDays <= minDays) maxDays = minDays + 1;

            var days = RandomMagic.Next(minDays, maxDays + 1);
            return TimeSpan.FromDays(days);
        }

        /// <summary>
        /// 获取随机小时数的缓存时间
        /// </summary>
        /// <param name="minHours">最小小时</param>
        /// <param name="maxHours">最大小时</param>
        /// <returns>随机TimeSpan</returns>
        public static TimeSpan GetRandomHours(int minHours = 1, int maxHours = 72)
        {
            if (minHours < 1) minHours = 1;
            if (maxHours <= minHours) maxHours = minHours + 1;

            var hours = RandomMagic.Next(minHours, maxHours + 1);
            return TimeSpan.FromHours(hours);
        }

        /// <summary>
        /// 获取随机分钟数的缓存时间
        /// </summary>
        /// <param name="minMinutes">最小分钟</param>
        /// <param name="maxMinutes">最大分钟</param>
        /// <returns>随机TimeSpan</returns>
        public static TimeSpan GetRandomMinutes(int minMinutes = 1, int maxMinutes = 1440)
        {
            if (minMinutes < 1) minMinutes = 1;
            if (maxMinutes <= minMinutes) maxMinutes = minMinutes + 1;

            var minutes = RandomMagic.Next(minMinutes, maxMinutes + 1);
            return TimeSpan.FromMinutes(minutes);
        }
    }
}